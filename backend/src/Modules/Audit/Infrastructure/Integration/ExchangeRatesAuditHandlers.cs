using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to ExchangeRates events (ADR-0010) — enriched manually: events are batched (the
/// result of a whole refresh run, several currency pairs at once), not tied to a single
/// aggregate/ChangeTracker row (see the comment in ExchangeRatesEvents.cs), so
/// DispatchDomainEventsInterceptor.TakeChanges is meaningless here.
///
/// entity_id = Guid.Empty — there is no single owning row for the event (several pairs per
/// run), but audit.audit_entries.entity_id NOT NULL requires some value (schema.md). Known
/// simplification: currencies are represented as CurrencyId (Guid), not as a code (e.g. "USD")
/// — enrichment via ReferenceData.Application.ICurrencyLookup is not implemented in this slice
/// for simplicity (see ADR-0010).
/// </summary>
public sealed class ExchangeRatesUpdatedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<ExchangeRatesUpdated>>
{
    public Task Handle(DomainEventNotification<ExchangeRatesUpdated> notification, CancellationToken cancellationToken)
    {
        var changes = notification.DomainEvent.UpdatedPairs
            .Select(pair => new AuditFieldChange(
                $"{pair.FromCurrencyId.Value}->{pair.ToCurrencyId.Value}", null, pair.Rate.ToString(CultureInfo.InvariantCulture)))
            .ToList();

        return recorder.RecordAsync("ExchangeRateQuote", Guid.Empty, "RatesUpdated", changes, cancellationToken);
    }
}

public sealed class ExchangeRateUpdateFailedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<ExchangeRateUpdateFailed>>
{
    public Task Handle(DomainEventNotification<ExchangeRateUpdateFailed> notification, CancellationToken cancellationToken)
    {
        var changes = notification.DomainEvent.FailedPairs
            .Select(pair => new AuditFieldChange($"{pair.FromCurrencyId.Value}->{pair.ToCurrencyId.Value}", null, pair.Reason))
            .ToList();

        return recorder.RecordAsync("ExchangeRateQuote", Guid.Empty, "RateUpdateFailed", changes, cancellationToken);
    }
}
