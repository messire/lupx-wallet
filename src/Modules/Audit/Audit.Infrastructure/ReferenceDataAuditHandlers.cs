using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ReferenceData.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>Подписчики на 9 событий ReferenceData (3 типа справочника x create/deactivate/delete), ADR-0010.</summary>
public sealed class WalletTypeCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletTypeCreated>>
{
    public Task Handle(DomainEventNotification<WalletTypeCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "WalletType", notification.DomainEvent.WalletTypeId.Value, "Created", cancellationToken);
}

public sealed class WalletTypeDeactivatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletTypeDeactivated>>
{
    public Task Handle(DomainEventNotification<WalletTypeDeactivated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "WalletType", notification.DomainEvent.WalletTypeId.Value, "Deactivated", cancellationToken);
}

public sealed class WalletTypeDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletTypeDeleted>>
{
    public Task Handle(DomainEventNotification<WalletTypeDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "WalletType", notification.DomainEvent.WalletTypeId.Value, "Deleted", cancellationToken);
}

public sealed class OperationTypeCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<OperationTypeCreated>>
{
    public Task Handle(DomainEventNotification<OperationTypeCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "OperationType", notification.DomainEvent.OperationTypeId.Value, "Created", cancellationToken);
}

public sealed class OperationTypeDeactivatedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<OperationTypeDeactivated>>
{
    public Task Handle(DomainEventNotification<OperationTypeDeactivated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "OperationType", notification.DomainEvent.OperationTypeId.Value, "Deactivated", cancellationToken);
}

public sealed class OperationTypeDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<OperationTypeDeleted>>
{
    public Task Handle(DomainEventNotification<OperationTypeDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "OperationType", notification.DomainEvent.OperationTypeId.Value, "Deleted", cancellationToken);
}

public sealed class CurrencyCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CurrencyCreated>>
{
    public Task Handle(DomainEventNotification<CurrencyCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Currency", notification.DomainEvent.CurrencyId.Value, "Created", cancellationToken);
}

public sealed class CurrencyDeactivatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CurrencyDeactivated>>
{
    public Task Handle(DomainEventNotification<CurrencyDeactivated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Currency", notification.DomainEvent.CurrencyId.Value, "Deactivated", cancellationToken);
}

public sealed class CurrencyDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CurrencyDeleted>>
{
    public Task Handle(DomainEventNotification<CurrencyDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Currency", notification.DomainEvent.CurrencyId.Value, "Deleted", cancellationToken);
}
