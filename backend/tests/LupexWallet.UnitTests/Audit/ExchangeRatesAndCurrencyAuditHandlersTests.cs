using FluentAssertions;
using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Audit;

/// <summary>
/// Audit subscribers added by ADR-0004 §7–§9 (ADR-0006): the history load pass is one batched
/// <c>HistoryLoaded</c> entry (entity_id = Guid.Empty, loaded ranges and failed pairs in the changes), and a
/// service rename of an active currency is <c>Updated</c> (the diff itself comes from the ChangeTracker and is
/// covered by the integration tests — here the routing).
/// </summary>
public sealed class ExchangeRatesAndCurrencyAuditHandlersTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Eur = new(Guid.NewGuid());

    private readonly IAuditEntryRepository _repository = Substitute.For<IAuditEntryRepository>();
    private readonly IAuditUnitOfWork _unitOfWork = Substitute.For<IAuditUnitOfWork>();
    private readonly List<AuditEntry> _recorded = [];
    private AuditActorContext _actor = AuditActorContext.User;

    private AuditRecorder CreateRecorder()
    {
        var actorAccessor = Substitute.For<IAuditActorAccessor>();
        actorAccessor.Current.Returns(_ => _actor);
        _repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => _recorded.Add(call.Arg<AuditEntry>()));
        return new AuditRecorder(_repository, _unitOfWork, actorAccessor, new DispatchDomainEventsInterceptor(Substitute.For<IDomainEventDispatcher>()));
    }

    [Fact]
    public async Task HistoryLoaded_RecordsOneEntryWithLoadedRangesAndFailedPairsUnderGuidEmpty()
    {
        var handler = new ExchangeRateHistoryLoadedAuditHandler(CreateRecorder());
        var historyLoaded = new ExchangeRateHistoryLoaded(
            DateTimeOffset.UtcNow,
            [new HistoryRangeLoaded(Usd, Eur, new DateOnly(2025, 1, 3), new DateOnly(2026, 10, 1))],
            [new ExchangeRatePairFailure(Eur, Usd, "Frankfurter недоступен")]);

        await handler.Handle(new DomainEventNotification<ExchangeRateHistoryLoaded>(historyLoaded), CancellationToken.None);

        var entry = _recorded.Should().ContainSingle("one record per pass").Subject;
        entry.EntityType.Should().Be("ExchangeRateQuote");
        entry.EntityId.Should().Be(Guid.Empty);
        entry.Action.Should().Be("HistoryLoaded");
        entry.Changes.Should().BeEquivalentTo(
        [
            new AuditFieldChange($"{Usd.Value}->{Eur.Value}", null, "2025-01-03..2026-10-01"),
            new AuditFieldChange($"{Eur.Value}->{Usd.Value}", null, "Frankfurter недоступен"),
        ]);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HistoryLoaded_PassRunBySystem_RecordsTheSystemActor()
    {
        _actor = new AuditActorContext(IsSystem: true, SystemProcessName: "ExchangeRateRefreshBackgroundService");
        var handler = new ExchangeRateHistoryLoadedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<ExchangeRateHistoryLoaded>(new ExchangeRateHistoryLoaded(
                DateTimeOffset.UtcNow, [new HistoryRangeLoaded(Usd, Eur, new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2))], [])),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.Actor.Kind.Should().Be(AuditActorKind.System);
        entry.Actor.SystemProcessName.Should().Be("ExchangeRateRefreshBackgroundService");
    }

    [Fact]
    public async Task CurrencyRenamed_RecordsUpdatedActionForCurrencyEntity()
    {
        var currencyId = CurrencyId.New();
        var handler = new CurrencyRenamedAuditHandler(CreateRecorder());

        await handler.Handle(new DomainEventNotification<CurrencyRenamed>(new CurrencyRenamed(currencyId)), CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Currency");
        entry.EntityId.Should().Be(currencyId.Value);
        entry.Action.Should().Be("Updated");
    }
}
