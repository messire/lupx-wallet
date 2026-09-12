using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to Operations events (ADR-0010). Unlike BalanceHistory.Infrastructure.
/// OperationEventHandlers, this module needs separate TransferCreated/TransferDeleted handlers:
/// BalanceHistory doesn't need them (both "legs" of a transfer already raise their own
/// OperationCreated/OperationDeleted), but Audit must create a separate record for the Transfer
/// itself (UC-25 — "record change history", including transfers), not only for its pair of
/// operations.
/// </summary>
public sealed class OperationCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<OperationCreated>>
{
    public Task Handle(DomainEventNotification<OperationCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Operation", notification.DomainEvent.OperationId.Value, "Created", cancellationToken);
}

public sealed class OperationUpdatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<OperationUpdated>>
{
    public Task Handle(DomainEventNotification<OperationUpdated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Operation", notification.DomainEvent.OperationId.Value, "Updated", cancellationToken);
}

public sealed class OperationDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<OperationDeleted>>
{
    public Task Handle(DomainEventNotification<OperationDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Operation", notification.DomainEvent.OperationId.Value, "Deleted", cancellationToken);
}

public sealed class TransferCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<TransferCreated>>
{
    public Task Handle(DomainEventNotification<TransferCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Transfer", notification.DomainEvent.TransferId.Value, "Created", cancellationToken);
}

public sealed class TransferDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<TransferDeleted>>
{
    public Task Handle(DomainEventNotification<TransferDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Transfer", notification.DomainEvent.TransferId.Value, "Deleted", cancellationToken);
}
