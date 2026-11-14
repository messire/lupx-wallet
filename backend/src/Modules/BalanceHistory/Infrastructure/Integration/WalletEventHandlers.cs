using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Subscribers to Wallets domain events (ADR-0002, §2) — in Infrastructure for the same reason
/// as OperationEventHandlers (DomainEventNotification + another module's Domain, ADR-0001, §2
/// way e); registered manually in BalanceHistoryModuleExtensions.
/// Snapshots are derived data, not wallet history: they are removed with the wallet and
/// rebuilt when its currency changes.
/// </summary>
internal static class WalletSnapshotRemoval
{
    /// <summary>
    /// Under the wallet advisory lock (serializes with the scheduled pass), marks every wallet
    /// snapshot as deleted (<see cref="BalanceSnapshotDeleted"/> for audit), removes it and saves.
    /// </summary>
    public static async Task RemoveAllAsync(
        IBalanceSnapshotRepository repository,
        IBalanceHistoryUnitOfWork unitOfWork,
        WalletId walletId,
        CancellationToken cancellationToken)
    {
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var snapshots = await repository.GetAllForWalletAsync(walletId, cancellationToken);
        if (snapshots.Count == 0)
        {
            return;
        }

        foreach (var snapshot in snapshots)
        {
            snapshot.MarkAsDeleted();
            repository.Remove(snapshot);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>ADR-0002, §2: deleting a wallet removes all its snapshots in the same transaction.</summary>
public sealed class WalletDeletedHandler(IBalanceSnapshotRepository repository, IBalanceHistoryUnitOfWork unitOfWork)
    : INotificationHandler<DomainEventNotification<WalletDeleted>>
{
    public Task Handle(DomainEventNotification<WalletDeleted> notification, CancellationToken cancellationToken) =>
        WalletSnapshotRemoval.RemoveAllAsync(repository, unitOfWork, notification.DomainEvent.WalletId, cancellationToken);
}

/// <summary>
/// ADR-0002, §2: a wallet without operations may change currency even if it has snapshots.
/// BalanceSnapshot.UpdateBalance rejects a currency change, so old snapshots are removed
/// (separate SaveChanges — same (WalletId, SnapshotDate) key must not be deleted and inserted in
/// one change set) and then rebuilt in the new currency from the accounting start date.
/// </summary>
public sealed class WalletCurrencyChangedHandler(
    IBalanceSnapshotRepository repository,
    IBalanceHistoryUnitOfWork unitOfWork,
    IWalletBalanceGateway walletGateway,
    BalanceRecalculationService recalculationService)
    : INotificationHandler<DomainEventNotification<WalletCurrencyChanged>>
{
    public async Task Handle(DomainEventNotification<WalletCurrencyChanged> notification, CancellationToken cancellationToken)
    {
        var walletId = notification.DomainEvent.WalletId;

        await WalletSnapshotRemoval.RemoveAllAsync(repository, unitOfWork, walletId, cancellationToken);

        var wallet = await walletGateway.GetAsync(walletId, cancellationToken);
        if (wallet is null)
        {
            return;
        }

        await recalculationService.RecalculateFromAsync(walletId, wallet.AccountingStartDate, cancellationToken);
    }
}
