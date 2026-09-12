using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Read-only contract published by Wallets for BalanceHistory (ADR-0008) — enumerates all
/// wallets that need daily balance snapshots (scheduled job and gap backfill, ADR-0004).
/// Archived wallets are included — archiving does not remove balance history (ADR-0002).
/// </summary>
public interface IWalletDirectory
{
    Task<IReadOnlyList<WalletId>> GetAllWalletIdsAsync(CancellationToken cancellationToken);
}
