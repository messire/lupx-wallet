using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// BackfillExchangeRateHistoryCommandHandler (ADR-0004 §9) on test doubles of the ports:
/// chunking (at most HistoryChunkDays, left edge from new dates to old, right edge from old to new, 7 days of
/// margin), per-pair coverage, empty chunks (they never end the left walk), 422 / Unavailable / concurrent-delete
/// handling, cleanup of stale pairs, the ExchangeRateHistoryLoaded event and the "no primary wallet" exit.
/// </summary>
public sealed class BackfillExchangeRateHistoryCommandHandlerTests
{
    private static readonly CurrencyId Eur = new(Guid.NewGuid());
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Gbp = new(Guid.NewGuid());

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IHistoricalExchangeRateRepository _history = Substitute.For<IHistoricalExchangeRateRepository>();
    private readonly IHistoricalRateCoverageRepository _coverageRepository = Substitute.For<IHistoricalRateCoverageRepository>();
    private readonly IExchangeRatesUnitOfWork _unitOfWork = Substitute.For<IExchangeRatesUnitOfWork>();
    private readonly IWalletCurrencySet _walletCurrencySet = Substitute.For<IWalletCurrencySet>();
    private readonly ICurrencyLookup _currencyLookup = Substitute.For<ICurrencyLookup>();
    private readonly IDomainEventDispatcher _eventDispatcher = Substitute.For<IDomainEventDispatcher>();
    private readonly ScriptedClient _client = new();
    private readonly Dictionary<(CurrencyId, CurrencyId), HistoricalRateCoverage> _coverageStore = [];
    private readonly List<HistoricalExchangeRate> _addedRates = [];
    private readonly List<ExchangeRateHistoryLoaded> _dispatched = [];

    public BackfillExchangeRateHistoryCommandHandlerTests()
    {
        _coverageRepository.FindAsync(Arg.Any<CurrencyId>(), Arg.Any<CurrencyId>(), Arg.Any<CancellationToken>())
            .Returns(call => _coverageStore.GetValueOrDefault((call.ArgAt<CurrencyId>(0), call.ArgAt<CurrencyId>(1))));
        _coverageRepository.When(r => r.Add(Arg.Any<HistoricalRateCoverage>()))
            .Do(call =>
            {
                var coverage = call.Arg<HistoricalRateCoverage>();
                _coverageStore[(coverage.FromCurrencyId, coverage.ToCurrencyId)] = coverage;
            });
        _history.GetRangeAsync(
                Arg.Any<CurrencyId>(), Arg.Any<CurrencyId>(), Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<HistoricalExchangeRate>)[]);
        _history.When(r => r.Add(Arg.Any<HistoricalExchangeRate>())).Do(call => _addedRates.Add(call.Arg<HistoricalExchangeRate>()));
        _eventDispatcher
            .When(d => d.DispatchAsync(Arg.Any<IEnumerable<IDomainEvent>>(), Arg.Any<CancellationToken>()))
            .Do(call => _dispatched.AddRange(call.Arg<IEnumerable<IDomainEvent>>().OfType<ExchangeRateHistoryLoaded>()));

        SetActiveCurrencies(Eur, Usd);
        SetWallets(primary: Eur, earliestStart: Today.AddDays(-30));
    }

    private BackfillExchangeRateHistoryCommandHandler CreateHandler(int chunkDays = ExchangeRateHistoryOptions.DefaultChunkDays) => new(
        _history, _coverageRepository, _unitOfWork, _client, _walletCurrencySet, _currencyLookup, _eventDispatcher,
        Options.Create(new ExchangeRateHistoryOptions { HistoryChunkDays = chunkDays }), NullLogger<BackfillExchangeRateHistoryCommandHandler>.Instance);

    private static string CodeOf(CurrencyId id) => id == Eur ? "EUR" : id == Usd ? "USD" : id == Gbp ? "GBP" : "XXX";

    private void SetActiveCurrencies(params CurrencyId[] ids) =>
        _currencyLookup.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<CurrencyLookupResult>)ids.Select(id => new CurrencyLookupResult(id, CodeOf(id))).ToList());

    private void SetWallets(CurrencyId? primary, DateOnly? earliestStart) =>
        _walletCurrencySet.GetAsync(Arg.Any<CancellationToken>())
            .Returns(new WalletCurrencySetResult(primary, primary is null ? [] : [primary.Value], earliestStart));

    private static HistoricalRateCoverage Coverage(CurrencyId from, DateOnly coveredFrom, DateOnly coveredTo) =>
        HistoricalRateCoverage.Create(from, Eur, coveredFrom, coveredTo, DateTimeOffset.UtcNow);

    private HistoricalRateCoverage Seed(HistoricalRateCoverage coverage)
    {
        _coverageStore[(coverage.FromCurrencyId, coverage.ToCurrencyId)] = coverage;
        return coverage;
    }

    private HistoricalRateCoverage CoverageOf(CurrencyId from) => _coverageStore[(from, Eur)];

    [Fact]
    public async Task Handle_NoPrimaryWallet_DoesNothingAndMakesNoRequests()
    {
        SetWallets(primary: null, earliestStart: null);

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        result.Should().BeSameAs(BackfillResult.Empty);
        _client.Requests.Should().BeEmpty();
        await _currencyLookup.DidNotReceiveWithAnyArgs().GetAllAsync(default);
        await _history.DidNotReceiveWithAnyArgs().RemoveStaleAsync(default, default!, default);
        await _eventDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task Handle_PrimaryCurrencyNotAmongActive_DoesNothing()
    {
        SetActiveCurrencies(Usd);

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        result.Loaded.Should().BeEmpty();
        _client.Requests.Should().BeEmpty();
        await _history.DidNotReceiveWithAnyArgs().RemoveStaleAsync(default, default!, default);
    }

    [Fact]
    public async Task Handle_NewPair_RequestsPeriodFromSevenDaysBeforeEarliestStartToToday()
    {
        SetWallets(Eur, Today.AddDays(-30));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(new ClientRequest("USD", "EUR", Today.AddDays(-37), Today));
        var coverage = CoverageOf(Usd);
        coverage.CoveredFrom.Should().Be(Today.AddDays(-37));
        coverage.CoveredTo.Should().Be(Today);
    }

    [Fact]
    public async Task Handle_PeriodLongerThanChunk_LoadsChunksFromNewestToOldestAndEachChunkSavesOnce()
    {
        // Default chunk of 366 days: [start − 7; today] = 408 days → two requests.
        SetWallets(Eur, Today.AddDays(-400));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(
            new ClientRequest("USD", "EUR", Today.AddDays(-365), Today),
            new ClientRequest("USD", "EUR", Today.AddDays(-407), Today.AddDays(-366)));
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-407));
        CoverageOf(Usd).CoveredTo.Should().Be(Today);
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConfiguredSmallChunk_SplitsLeftEdgeIntoContiguousChunksNoLongerThanConfigured()
    {
        SetWallets(Eur, Today.AddDays(-250)); // required from = today − 257 → 258 days

        await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(
            new ClientRequest("USD", "EUR", Today.AddDays(-99), Today),
            new ClientRequest("USD", "EUR", Today.AddDays(-199), Today.AddDays(-100)),
            new ClientRequest("USD", "EUR", Today.AddDays(-257), Today.AddDays(-200)));
        _client.Requests.Should().OnlyContain(r => r.To.DayNumber - r.From.DayNumber + 1 <= 100);
    }

    [Fact]
    public async Task Handle_NoEarliestStartDate_UsesTodayMinusSevenDaysAsTheLeftBorder()
    {
        // Primary currency known but no wallet date (not reachable in practice — a primary wallet has one):
        // the period degrades to [today − 7; today] instead of an unbounded one.
        SetWallets(Eur, earliestStart: null);

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(new ClientRequest("USD", "EUR", Today.AddDays(-7), Today));
    }

    [Fact]
    public async Task Handle_EarliestStartInTheFuture_IsClampedToToday()
    {
        SetWallets(Eur, Today.AddDays(10));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(new ClientRequest("USD", "EUR", Today.AddDays(-7), Today));
    }

    [Fact]
    public async Task Handle_NewPair_StoresReturnedRatesAndWritesNothingForPrimaryCurrency()
    {
        SetWallets(Eur, Today.AddDays(-3));
        _client.Respond = request => request.From.DayNumber == Today.AddDays(-10).DayNumber
            ? [(Today.AddDays(-10), 1.10m), (Today.AddDays(-9), 1.11m)]
            : [];

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _addedRates.Select(r => (r.FromCurrencyId, r.ToCurrencyId, r.RateDate, r.Rate.Rate)).Should().Equal(
            (Usd, Eur, Today.AddDays(-10), 1.10m),
            (Usd, Eur, Today.AddDays(-9), 1.11m));
        _client.Requests.Should().NotContain(r => r.FromCode == "EUR", "the primary currency has no pair to itself");
    }

    [Fact]
    public async Task Handle_EarliestStartMovedLeft_LoadsOnlyTheMissingLeftPart()
    {
        SetWallets(Eur, Today.AddDays(-60)); // required from = today − 67
        Seed(Coverage(Usd, Today.AddDays(-37), Today));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(new ClientRequest("USD", "EUR", Today.AddDays(-67), Today.AddDays(-38)));
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-67));
        CoverageOf(Usd).CoveredTo.Should().Be(Today);
    }

    [Fact]
    public async Task Handle_CoverageEndsBeforeToday_RequestsOnlyRightEdgeStartingSevenDaysBackFromCoveredTo()
    {
        SetWallets(Eur, Today.AddDays(-30));
        Seed(Coverage(Usd, Today.AddDays(-37), Today.AddDays(-1)));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(
            new ClientRequest("USD", "EUR", Today.AddDays(-8), Today));
        CoverageOf(Usd).CoveredTo.Should().Be(Today);
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-37));
    }

    [Fact]
    public async Task Handle_LongGapOnTheRight_LoadsChunksFromOldToNewWithoutHoles()
    {
        SetWallets(Eur, Today.AddDays(-300));
        Seed(Coverage(Usd, Today.AddDays(-307), Today.AddDays(-250)));

        await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(
            new ClientRequest("USD", "EUR", Today.AddDays(-257), Today.AddDays(-158)),
            new ClientRequest("USD", "EUR", Today.AddDays(-157), Today.AddDays(-58)),
            new ClientRequest("USD", "EUR", Today.AddDays(-57), Today));
        CoverageOf(Usd).CoveredTo.Should().Be(Today);
    }

    [Fact]
    public async Task Handle_RightEdgeOverlapNeverReachesLeftOfTheCoveredFrom()
    {
        SetWallets(Eur, Today.AddDays(-30));
        // Coverage shorter than the 7-day overlap: the request must not start before covered_from.
        Seed(Coverage(Usd, Today.AddDays(-37), Today.AddDays(-35)));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests[0].From.Should().Be(Today.AddDays(-37));
    }

    [Fact]
    public async Task Handle_PairOneDayBehind_CostsOneRequestThenTheNextPassCostsNothing()
    {
        SetWallets(Eur, Today.AddDays(-30));
        Seed(Coverage(Usd, Today.AddDays(-37), Today.AddDays(-1)));

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);
        _client.Requests.Should().ContainSingle("the right edge: covered_to − 7 … today, one request per pair");

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().ContainSingle("the coverage reaches today and the left border, nothing is missing");
    }

    [Fact]
    public async Task Handle_FullyCoveredPair_MakesNoRequestAndNoSave()
    {
        SetWallets(Eur, Today.AddDays(-30));
        Seed(Coverage(Usd, Today.AddDays(-40), Today));

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().BeEmpty();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        result.Loaded.Should().BeEmpty();
        await _eventDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task Handle_EmptyLeftChunksWithExistingCoverage_WalkToRequiredFromInsteadOfStopping()
    {
        SetWallets(Eur, Today.AddDays(-300)); // required from = today − 307
        Seed(Coverage(Usd, Today.AddDays(-100), Today));
        _client.Respond = _ => [];

        await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().Equal(
            new ClientRequest("USD", "EUR", Today.AddDays(-200), Today.AddDays(-101)),
            new ClientRequest("USD", "EUR", Today.AddDays(-300), Today.AddDays(-201)),
            new ClientRequest("USD", "EUR", Today.AddDays(-307), Today.AddDays(-301)));
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-307), "a gap in the data is not the end of the data");
        await _unitOfWork.Received(3).SaveChangesAsync(Arg.Any<CancellationToken>());
        _addedRates.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DataOnlyInOlderChunks_NewestEmptyChunksStillCreateCoverageAndOlderDataIsLoaded()
    {
        SetWallets(Eur, Today.AddDays(-250)); // required from = today − 257, chunks of 100
        _client.Respond = request => request.To <= Today.AddDays(-100) ? [(request.To, 1.2m)] : [];

        var result = await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().HaveCount(3, "the walk goes on to requiredFrom after an empty first chunk");
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-257));
        CoverageOf(Usd).CoveredTo.Should().Be(Today, "the first (empty) chunk created the coverage up to today");
        _addedRates.Select(r => r.RateDate).Should().BeEquivalentTo([Today.AddDays(-100), Today.AddDays(-200)]);
        result.Loaded.Should().ContainSingle().Which.Should().Be(new HistoryRangeLoaded(Usd, Eur, Today.AddDays(-257), Today.AddDays(-100)));
    }

    [Fact]
    public async Task Handle_NewestChunkHasDataThenEmptyOnes_CoverageReachesRequiredFromAndAllChunksAreAsked()
    {
        SetWallets(Eur, Today.AddDays(-250)); // required from = today − 257, chunks of 100
        _client.Respond = request => request.To == Today ? [(Today, 1.2m)] : [];

        var result = await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().HaveCount(3);
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-257));
        CoverageOf(Usd).CoveredTo.Should().Be(Today);
        result.Loaded.Should().ContainSingle().Which.Should().Be(new HistoryRangeLoaded(Usd, Eur, Today.AddDays(-99), Today));
    }

    [Fact]
    public async Task Handle_FirstChunkEmptyForNewPair_CreatesCoverageWithoutRatesAndReportsNothingLoaded()
    {
        _client.Respond = _ => [];

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().ContainSingle();
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-37));
        CoverageOf(Usd).CoveredTo.Should().Be(Today);
        _addedRates.Should().BeEmpty();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        result.Loaded.Should().BeEmpty();
        await _eventDispatcher.DidNotReceiveWithAnyArgs().DispatchAsync(default!, default);
    }

    [Fact]
    public async Task Handle_EmptyRightChunk_DoesNotMoveCoverageAndDoesNotSave()
    {
        SetWallets(Eur, Today.AddDays(-30));
        var coverage = Seed(Coverage(Usd, Today.AddDays(-37), Today.AddDays(-5)));
        _client.Respond = _ => [];

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().ContainSingle();
        coverage.CoveredTo.Should().Be(Today.AddDays(-5), "a delisted currency costs one request per pass and the coverage is not advanced over nothing");
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MiddleRightChunkEmptyThenData_CoverageJumpsOverTheEmptyChunk()
    {
        SetWallets(Eur, Today.AddDays(-300));
        var coverage = Seed(Coverage(Usd, Today.AddDays(-307), Today.AddDays(-250)));
        _client.Respond = request => request.To == Today ? [(Today, 1.3m)] : [];

        await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().HaveCount(3);
        coverage.CoveredTo.Should().Be(Today);
        _addedRates.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_UnsupportedCurrency_SkipsThePairWithoutFailureAndContinuesWithOthers()
    {
        SetActiveCurrencies(Eur, Usd, Gbp);
        _client.Throw = request => request.FromCode == "USD" ? new FrankfurterUnsupportedCurrencyException("USD", "EUR") : null;
        _client.Respond = _ => [(Today, 0.8m)];

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        result.Failed.Should().BeEmpty("422 is a permanent property of the pair, not a source failure");
        result.Loaded.Should().ContainSingle(l => l.FromCurrencyId == Gbp);
        _client.Requests.Select(r => r.FromCode).Should().Equal("USD", "GBP");
        _coverageStore.Keys.Should().ContainSingle().Which.Should().Be((Gbp, Eur));
    }

    [Fact]
    public async Task Handle_SourceUnavailableMidPair_StopsThePassKeepsSavedChunksAndReportsFailure()
    {
        SetActiveCurrencies(Eur, Usd, Gbp);
        SetWallets(Eur, Today.AddDays(-250)); // three chunks of 100
        _client.Throw = request => request.From < Today.AddDays(-150) ? new FrankfurterUnavailableException("down") : null;
        _client.Respond = _ => [(Today, 1.0m)];

        var result = await CreateHandler(chunkDays: 100).Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Should().OnlyContain(r => r.FromCode == "USD", "the second currency is not asked once the source is down");
        _client.Requests.Should().HaveCount(2);
        CoverageOf(Usd).CoveredFrom.Should().Be(Today.AddDays(-99), "the chunk loaded before the failure stays covered");
        _coverageStore.Should().NotContainKey((Gbp, Eur));
        result.Failed.Should().ContainSingle(f => f.FromCurrencyId == Usd && f.ToCurrencyId == Eur && f.Reason == "down");
        result.Loaded.Should().ContainSingle(l => l.FromCurrencyId == Usd && l.RangeFrom == Today.AddDays(-99) && l.RangeTo == Today);
        _dispatched.Should().ContainSingle("the report contains the saved range and the failure");
        _dispatched[0].Failed.Should().HaveCount(1);
        _dispatched[0].Loaded.Should().HaveCount(1);
        await _history.Received(1).RemoveStaleAsync(Eur, Arg.Any<IReadOnlyCollection<CurrencyId>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ConcurrentDeletionOfTheCurrency_SkipsThePairAndContinues()
    {
        SetActiveCurrencies(Eur, Usd, Gbp);
        _client.Respond = _ => [(Today, 0.8m)];
        var saves = 0;
        _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(_ =>
            ++saves == 1 ? Task.FromException(new DbUpdateConcurrencyException("row vanished")) : Task.CompletedTask);

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _client.Requests.Select(r => r.FromCode).Should().Equal("USD", "GBP");
        result.Failed.Should().BeEmpty();
        result.Loaded.Should().ContainSingle(l => l.FromCurrencyId == Gbp);
    }

    [Fact]
    public async Task Handle_ChangeTrackerIsClearedAfterEveryPairIncludingFailedOnes()
    {
        SetActiveCurrencies(Eur, Usd, Gbp);
        _client.Throw = request => request.FromCode == "USD" ? new FrankfurterUnsupportedCurrencyException("USD", "EUR") : null;
        _client.Respond = _ => [(Today, 0.8m)];

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _unitOfWork.Received(2).ClearChangeTracker();
    }

    [Fact]
    public async Task Handle_ChangeTrackerIsClearedWhenTheSourceIsUnavailable()
    {
        _client.Throw = _ => new FrankfurterUnavailableException("down");

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _unitOfWork.Received(1).ClearChangeTracker();
    }

    [Fact]
    public async Task Handle_Always_RemovesStaleHistoryAndCoverageForCurrentPrimaryAndActiveCurrencies()
    {
        SetActiveCurrencies(Eur, Usd, Gbp);
        _client.Respond = _ => [];

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        Func<IReadOnlyCollection<CurrencyId>, bool> isActiveSet = ids => ids.ToHashSet().SetEquals([Eur, Usd, Gbp]);
        await _history.Received(1).RemoveStaleAsync(Eur, Arg.Is<IReadOnlyCollection<CurrencyId>>(ids => isActiveSet(ids)), Arg.Any<CancellationToken>());
        await _coverageRepository.Received(1).RemoveStaleAsync(Eur, Arg.Is<IReadOnlyCollection<CurrencyId>>(ids => isActiveSet(ids)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_SomethingLoaded_RaisesOneHistoryLoadedEventWithRangesPerPair()
    {
        SetActiveCurrencies(Eur, Usd, Gbp);
        SetWallets(Eur, Today.AddDays(-3));
        _client.Respond = _ => [(Today, 0.8m)];

        var result = await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        var @event = _dispatched.Should().ContainSingle("one record per pass, not per pair").Subject;
        @event.Failed.Should().BeEmpty();
        @event.Loaded.Should().BeEquivalentTo(
        [
            new HistoryRangeLoaded(Usd, Eur, Today.AddDays(-10), Today),
            new HistoryRangeLoaded(Gbp, Eur, Today.AddDays(-10), Today),
        ]);
        result.Loaded.Should().BeEquivalentTo(@event.Loaded);
    }

    [Fact]
    public async Task Handle_StoredDateWithChangedRate_UpdatesItInsteadOfAddingAndSkipsUnchangedOnes()
    {
        SetWallets(Eur, Today.AddDays(-3));
        var fetchedAt = DateTimeOffset.UtcNow.AddDays(-2);
        var unchanged = HistoricalExchangeRate.Create(Usd, Eur, Today.AddDays(-2), new ExchangeRateValue(1.10m), fetchedAt);
        var changed = HistoricalExchangeRate.Create(Usd, Eur, Today.AddDays(-1), new ExchangeRateValue(1.11m), fetchedAt);
        _history.GetRangeAsync(Usd, Eur, Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<HistoricalExchangeRate>)[unchanged, changed]);
        _client.Respond = _ => [(Today.AddDays(-2), 1.10m), (Today.AddDays(-1), 1.15m), (Today, 1.16m)];

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        unchanged.FetchedAt.Should().Be(fetchedAt, "UpdateRate is not called when the rate did not change");
        changed.Rate.Rate.Should().Be(1.15m);
        changed.FetchedAt.Should().BeAfter(fetchedAt);
        _addedRates.Should().ContainSingle().Which.RateDate.Should().Be(Today);
    }

    [Fact]
    public async Task Handle_ServiceReturnsDatesOutsideTheChunkOrRepeatedDates_IgnoresOutsideAndLastWins()
    {
        SetWallets(Eur, Today.AddDays(-3)); // chunk [today − 10; today]
        _client.Respond = _ =>
        [
            (Today.AddDays(-30), 9.9m),
            (Today.AddDays(-5), 1.0m),
            (Today.AddDays(-5), 1.5m),
            (Today.AddDays(5), 9.9m),
        ];

        await CreateHandler().Handle(new BackfillExchangeRateHistoryCommand(), CancellationToken.None);

        _addedRates.Should().ContainSingle().Which.Should().Match<HistoricalExchangeRate>(
            r => r.RateDate == Today.AddDays(-5) && r.Rate.Rate == 1.5m);
    }

    /// <summary>One <c>GetRatesForPeriodAsync</c> call.</summary>
    private sealed record ClientRequest(string FromCode, string ToCode, DateOnly From, DateOnly To);

    /// <summary>IFrankfurterClient whose answer to a period request is scripted per test; remembers every request.</summary>
    private sealed class ScriptedClient : IFrankfurterClient
    {
        public List<ClientRequest> Requests { get; } = [];

        /// <summary>Rates returned for a request; by default the service has one rate, on the last day of the requested chunk.</summary>
        public Func<ClientRequest, IReadOnlyList<(DateOnly Date, decimal Rate)>> Respond { get; set; } = request => [(request.To, 1.0m)];

        /// <summary>Exception to throw for a request, or null.</summary>
        public Func<ClientRequest, Exception?> Throw { get; set; } = _ => null;

        public Task<IReadOnlyList<(DateOnly Date, decimal Rate)>> GetRatesForPeriodAsync(
            string fromCurrencyCode, string toCurrencyCode, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
        {
            var request = new ClientRequest(fromCurrencyCode, toCurrencyCode, fromDate, toDate);
            Requests.Add(request);
            var exception = Throw(request);
            return exception is null
                ? Task.FromResult(Respond(request))
                : Task.FromException<IReadOnlyList<(DateOnly Date, decimal Rate)>>(exception);
        }

        public Task<decimal> GetLatestRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken) =>
            throw new NotSupportedException("The history load must not use the latest-rate endpoint.");

        public Task<IReadOnlyList<FrankfurterCurrency>> GetCurrenciesAsync(CancellationToken cancellationToken) =>
            throw new NotSupportedException("The history load must not reload the available list.");
    }
}
