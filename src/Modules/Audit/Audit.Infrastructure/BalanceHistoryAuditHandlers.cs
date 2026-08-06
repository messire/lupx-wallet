using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Подписчики на события BalanceHistory (ADR-0010) — обогащение вручную из полей события,
/// а не через DispatchDomainEventsInterceptor.TakeChanges: BalanceSnapshot идентифицируется
/// составным ключом (WalletId, SnapshotDate) без суррогатного Id, а SnapshotDate — часть
/// первичного ключа, поэтому базовый ChangeTracker-diff (исключающий ключевые свойства) не
/// включил бы её в changes — несколько пересчитанных дат одного кошелька в одной транзакции
/// стали бы неотличимы друг от друга. entity_id = WalletId (владелец истории), SnapshotDate —
/// явное поле в changes.
///
/// ddd-model.md §6 требует одну AuditEntry на каждый пересчитанный слепок (не агрегированную
/// запись на весь прогон каскадного пересчёта) — см. ADR-0010, раздел про объём аудита.
/// </summary>
public sealed class BalanceSnapshotCreatedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<BalanceSnapshotCreated>>
{
    public Task Handle(DomainEventNotification<BalanceSnapshotCreated> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        IReadOnlyList<AuditFieldChange> changes =
        [
            new AuditFieldChange("SnapshotDate", null, domainEvent.SnapshotDate.ToString("O", CultureInfo.InvariantCulture)),
            new AuditFieldChange("Balance", null, domainEvent.Balance.Amount.ToString(CultureInfo.InvariantCulture)),
        ];

        return recorder.RecordAsync("BalanceSnapshot", domainEvent.WalletId.Value, "SnapshotCreated", changes, cancellationToken);
    }
}

public sealed class BalanceSnapshotUpdatedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<BalanceSnapshotUpdated>>
{
    public Task Handle(DomainEventNotification<BalanceSnapshotUpdated> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        var snapshotDate = domainEvent.SnapshotDate.ToString("O", CultureInfo.InvariantCulture);
        IReadOnlyList<AuditFieldChange> changes =
        [
            new AuditFieldChange("SnapshotDate", snapshotDate, snapshotDate),
            new AuditFieldChange(
                "Balance",
                domainEvent.OldBalance.Amount.ToString(CultureInfo.InvariantCulture),
                domainEvent.NewBalance.Amount.ToString(CultureInfo.InvariantCulture)),
        ];

        return recorder.RecordAsync("BalanceSnapshot", domainEvent.WalletId.Value, "SnapshotUpdated", changes, cancellationToken);
    }
}
