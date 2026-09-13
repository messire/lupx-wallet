using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Application;

public interface IBalanceHistoryUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serializes concurrent recalculations of the same wallet (Postgres advisory
    /// transaction lock — released automatically when the transaction ends). Without this,
    /// two concurrent RecalculateFromAsync calls for the same wallet (e.g. a retroactive
    /// operation edit plus a concurrent scheduled run at 12:00 UTC) could both see "no
    /// snapshot for date D" and try to insert it — the second INSERT would fail on the
    /// (wallet_id, snapshot_date) primary key and roll back the whole command, including the
    /// original operation, which was valid on its own.
    /// </summary>
    Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken);
}
