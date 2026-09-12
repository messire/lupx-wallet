using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to Wallets domain events (ADR-0010, following ADR-0008) — live in
/// Infrastructure (reference DomainEventNotification&lt;T&gt; and Wallets.Domain, which the
/// Application layer must not reference, high-level-architecture.md, §2). Registered manually
/// in AuditModuleExtensions.
/// </summary>
public sealed class WalletCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletCreated>>
{
    public Task Handle(DomainEventNotification<WalletCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "Created", cancellationToken);
}

public sealed class WalletUpdatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletUpdated>>
{
    public Task Handle(DomainEventNotification<WalletUpdated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "Updated", cancellationToken);
}

public sealed class WalletArchivedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletArchived>>
{
    public Task Handle(DomainEventNotification<WalletArchived> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "Archived", cancellationToken);
}

public sealed class WalletDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletDeleted>>
{
    public Task Handle(DomainEventNotification<WalletDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "Deleted", cancellationToken);
}

public sealed class PrimaryWalletChangedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<PrimaryWalletChanged>>
{
    public Task Handle(DomainEventNotification<PrimaryWalletChanged> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, "Wallet", notification.DomainEvent.NewPrimaryWalletId.Value, "PrimaryChanged", cancellationToken);
}

public sealed class WalletCurrencyChangedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>
{
    public Task Handle(DomainEventNotification<WalletCurrencyChanged> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "CurrencyChanged", cancellationToken);
}

/// <summary>
/// Not in the plan's literal list (docs/PROGRESS.md, W2.2 "Task"), but covered by "ALL domain
/// events" of the Wallets module, as the plan itself requires (W2.2 scope) and ddd-model.md §5
/// ("any mutating action ... must produce an AuditEntry") — the effect on wallet balance
/// (Operations → IWalletBalanceGateway, ADR-0007) is itself a mutation of the Wallet record,
/// separate from OperationCreated (a different aggregate).
/// </summary>
public sealed class WalletBalanceChangedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletBalanceChanged>>
{
    public Task Handle(DomainEventNotification<WalletBalanceChanged> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "BalanceChanged", cancellationToken);
}
