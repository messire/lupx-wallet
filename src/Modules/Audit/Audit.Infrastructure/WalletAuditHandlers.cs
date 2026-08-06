using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Подписчики на доменные события Wallets (ADR-0010, по образцу ADR-0008) — живут в
/// Infrastructure (ссылаются на DomainEventNotification&lt;T&gt; и Wallets.Domain,
/// Application-слой на это ссылаться не должен, high-level-architecture.md, §2).
/// Регистрируются вручную в AuditModuleExtensions.
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
/// Не входит в буквальный перечень плана (docs/PROGRESS.md, W2.2 «Задача»), но входит в
/// «ВСЕ доменные события» модуля Wallets, как явно требует и сам план (W2.2 scope) и
/// ddd-model.md §5 («любое мутирующее действие... обязано породить AuditEntry») — эффект на
/// баланс кошелька (Operations → IWalletBalanceGateway, ADR-0007) сам по себе является
/// мутацией записи Wallet, отдельной от OperationCreated (другой агрегат).
/// </summary>
public sealed class WalletBalanceChangedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<WalletBalanceChanged>>
{
    public Task Handle(DomainEventNotification<WalletBalanceChanged> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, "Wallet", notification.DomainEvent.WalletId.Value, "BalanceChanged", cancellationToken);
}
