using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Подписчики на события ExchangeRates (ADR-0010) — обогащение вручную: события батчевые
/// (результат целого прогона обновления, несколько валютных пар за раз), не привязаны к
/// одному агрегату/одной строке ChangeTracker (см. комментарий в ExchangeRatesEvents.cs),
/// поэтому DispatchDomainEventsInterceptor.TakeChanges здесь бессмыслен.
///
/// entity_id = Guid.Empty — нет единственной строки-владельца события (несколько пар за
/// прогон), но audit.audit_entries.entity_id NOT NULL требует какое-то значение (schema.md).
/// Известное упрощение: валюты представлены как CurrencyId (Guid), не как код (например,
/// "USD") — обогащение через ReferenceData.Application.ICurrencyLookup не выполнено в этом
/// срезе ради простоты (см. ADR-0010).
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
