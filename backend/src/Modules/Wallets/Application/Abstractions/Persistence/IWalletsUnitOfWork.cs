using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>Thin abstraction over the module DbContext's SaveChangesAsync; command handlers call it explicitly at the end of processing.</summary>
public interface IWalletsUnitOfWork
{
    /// <summary>
    /// Takes the per-wallet advisory lock shared with Operations and BalanceHistory. Commands whose
    /// subscribers take this lock inside the same transaction must call it before touching the
    /// wallet row, otherwise they deadlock with commands that lock in the opposite order.
    /// </summary>
    Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
