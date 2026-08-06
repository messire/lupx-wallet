using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Подписчики на события Operations (ADR-0010). В отличие от BalanceHistory.Infrastructure.
/// OperationEventHandlers, здесь нужны отдельные обработчики TransferCreated/TransferDeleted:
/// BalanceHistory не нуждается в них (обе "ноги" перевода уже поднимают свои
/// OperationCreated/OperationDeleted), но Audit обязан завести отдельную запись для самого
/// Transfer как записи (UC-25 — "история изменений записи", включая переводы), не только для
/// пары его операций.
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
