using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Per-wallet transaction-scoped PostgreSQL advisory lock shared by every module that changes a
/// wallet's operations or balance history (ADR-0001, §4; ADR-0003, §2). The key is
/// <c>hashtextextended(&lt;walletId&gt;, 0)</c>, identical in all callers, and all DbContexts of one
/// command share the transaction's connection, so a repeated acquisition by the same transaction
/// is re-entrant. Held until the transaction ends.
/// </summary>
public static class WalletAdvisoryLock
{
    public static Task AcquireAsync(DatabaseFacade database, WalletId walletId, CancellationToken cancellationToken) =>
        database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({walletId.Value.ToString()}, 0))", cancellationToken);

    /// <summary>
    /// Acquires the locks of all given wallets sequentially in ascending <see cref="Guid"/> order
    /// (duplicates skipped). A fixed order keeps two commands touching the same pair of wallets
    /// in opposite directions from deadlocking.
    /// </summary>
    public static async Task AcquireAllAsync(
        DatabaseFacade database, IEnumerable<WalletId> walletIds, CancellationToken cancellationToken)
    {
        foreach (var walletId in walletIds.Select(x => x.Value).Distinct().Order())
        {
            await AcquireAsync(database, new WalletId(walletId), cancellationToken);
        }
    }
}
