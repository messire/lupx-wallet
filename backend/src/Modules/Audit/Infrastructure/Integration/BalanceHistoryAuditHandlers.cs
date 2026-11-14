using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to BalanceHistory events (ADR-0006) — enriched manually from event fields
/// instead of via DispatchDomainEventsInterceptor.TakeChanges: BalanceSnapshot is identified by
/// a composite key (WalletId, SnapshotDate) with no surrogate Id, and SnapshotDate is part of
/// the primary key, so the default ChangeTracker diff (which excludes key properties) would not
/// include it in changes — several recalculated dates for the same wallet in one transaction
/// would become indistinguishable. entity_id = WalletId (history owner), SnapshotDate is an
/// explicit field in changes.
///
/// ddd-model.md §6 requires one AuditEntry per recalculated snapshot (not one aggregated record
/// per cascading recalculation run) — see ADR-0006, audit scope section.
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

/// <summary>
/// One record per deleted snapshot (ADR-0002 §2): raised when a wallet is deleted or its
/// currency changes and the history is rebuilt.
/// </summary>
public sealed class BalanceSnapshotDeletedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<BalanceSnapshotDeleted>>
{
    public Task Handle(DomainEventNotification<BalanceSnapshotDeleted> notification, CancellationToken cancellationToken)
    {
        var domainEvent = notification.DomainEvent;
        IReadOnlyList<AuditFieldChange> changes =
        [
            new AuditFieldChange("SnapshotDate", domainEvent.SnapshotDate.ToString("O", CultureInfo.InvariantCulture), null),
            new AuditFieldChange("Balance", domainEvent.Balance.Amount.ToString(CultureInfo.InvariantCulture), null),
        ];

        return recorder.RecordAsync("BalanceSnapshot", domainEvent.WalletId.Value, "SnapshotDeleted", changes, cancellationToken);
    }
}
