using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// ExchangeRateLookup (ADR-0004 §8, PROGRESS R15) reads the database only: no rate service, no writes.
/// The class has no IFrankfurterClient dependency at all (checked by the constructor shape below).
/// </summary>
public sealed class ExchangeRateLookupTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Eur = new(Guid.NewGuid());

    private readonly ILatestExchangeRateRepository _latest = Substitute.For<ILatestExchangeRateRepository>();
    private readonly IHistoricalExchangeRateRepository _historical = Substitute.For<IHistoricalExchangeRateRepository>();

    private ExchangeRateLookup CreateLookup() => new(_latest, _historical);

    [Fact]
    public async Task GetHistoricalRate_SameCurrency_ReturnsOneForTheRequestedDateWithoutReadingStorage()
    {
        var date = new DateOnly(2026, 3, 14);

        var result = await CreateLookup().GetHistoricalRateAsync(Usd, Usd, date, CancellationToken.None);

        result.Should().Be((1m, date));
        await _historical.DidNotReceiveWithAnyArgs().FindClosestBeforeAsync(default, default, default, default);
    }

    [Fact]
    public async Task GetHistoricalRate_StoredClosestEarlierDate_ReturnsItsRateAndActualDate()
    {
        var saturday = new DateOnly(2026, 3, 14);
        var friday = saturday.AddDays(-1);
        _historical.FindClosestBeforeAsync(Usd, Eur, saturday, Arg.Any<CancellationToken>())
            .Returns(HistoricalExchangeRate.Create(Usd, Eur, friday, new ExchangeRateValue(0.93m), DateTimeOffset.UtcNow));

        var result = await CreateLookup().GetHistoricalRateAsync(Usd, Eur, saturday, CancellationToken.None);

        result.Should().Be((0.93m, friday), "RatesAsOfDate is the date of the rate actually applied");
    }

    [Fact]
    public async Task GetHistoricalRate_NothingStoredOnOrBeforeTheDate_ReturnsNull()
    {
        _historical.FindClosestBeforeAsync(Usd, Eur, Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns((HistoricalExchangeRate?)null);

        var result = await CreateLookup().GetHistoricalRateAsync(Usd, Eur, new DateOnly(2026, 3, 14), CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetRate_SameCurrency_ReturnsOneAndStoredQuoteOtherwise()
    {
        _latest.FindAsync(Usd, Eur, Arg.Any<CancellationToken>())
            .Returns(ExchangeRateQuote.Create(Usd, Eur, new ExchangeRateValue(0.91m), DateTimeOffset.UtcNow));

        (await CreateLookup().GetRateAsync(Usd, Usd, CancellationToken.None)).Should().Be(1m);
        (await CreateLookup().GetRateAsync(Usd, Eur, CancellationToken.None)).Should().Be(0.91m);
        (await CreateLookup().GetRateAsync(Eur, Usd, CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public void Lookup_DependsOnReadRepositoriesOnly_NotOnTheRateService()
    {
        var parameterTypes = typeof(ExchangeRateLookup).GetConstructors().Single().GetParameters().Select(p => p.ParameterType).ToList();

        parameterTypes.Should().BeEquivalentTo([typeof(ILatestExchangeRateRepository), typeof(IHistoricalExchangeRateRepository)]);
    }
}

/// <summary>Subscribers that only request a background pass (ADR-0004 §5, §9); the transaction-aware delivery is the signal's job.</summary>
public sealed class RefreshSignalSubscribersTests
{
    private readonly IExchangeRateRefreshSignal _signal = Substitute.For<IExchangeRateRefreshSignal>();

    [Fact]
    public async Task CurrencyCreated_RequestsARefresh()
    {
        await new CurrencyCreatedHandler(_signal).Handle(
            new DomainEventNotification<CurrencyCreated>(new CurrencyCreated(new CurrencyId(Guid.NewGuid()))),
            CancellationToken.None);

        _signal.Received(1).RequestRefresh();
        _signal.DidNotReceive().NotifyManualRefreshCompleted();
    }

    [Fact]
    public async Task WalletCreated_RequestsARefresh()
    {
        await new WalletCreatedHandler(_signal).Handle(
            new DomainEventNotification<WalletCreated>(new WalletCreated(WalletId.New())),
            CancellationToken.None);

        _signal.Received(1).RequestRefresh();
    }

    [Fact]
    public async Task WalletCurrencyChanged_RequestsARefresh()
    {
        await new WalletCurrencyChangedHandler(_signal).Handle(
            new DomainEventNotification<WalletCurrencyChanged>(
                new WalletCurrencyChanged(WalletId.New(), new CurrencyId(Guid.NewGuid()), new CurrencyId(Guid.NewGuid()))),
            CancellationToken.None);

        _signal.Received(1).RequestRefresh();
    }

    [Fact]
    public async Task PrimaryWalletChanged_RequestsARefresh()
    {
        await new PrimaryWalletChangedHandler(_signal).Handle(
            new DomainEventNotification<PrimaryWalletChanged>(new PrimaryWalletChanged(WalletId.New(), null)),
            CancellationToken.None);

        _signal.Received(1).RequestRefresh();
    }
}
