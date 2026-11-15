using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Application;

/// <summary>Storage of PlannedTransaction aggregates; every read returns the plan with its overrides and executions.</summary>
public interface IPlannedTransactionRepository
{
    void Add(PlannedTransaction plan);
    void Remove(PlannedTransaction plan);

    Task<PlannedTransaction?> GetByIdAsync(PlannedTransactionId id, CancellationToken cancellationToken);

    /// <summary>The plan's default wallet read without locking; null when the plan does not exist.</summary>
    Task<(bool Exists, WalletId? WalletId)> FindWalletIdAsync(PlannedTransactionId id, CancellationToken cancellationToken);

    /// <summary>Locks the plan row until the transaction ends, so commands on one plan run one after another.</summary>
    Task<PlannedTransaction?> GetForUpdateAsync(PlannedTransactionId id, CancellationToken cancellationToken);

    /// <summary>Ordered by creation time.</summary>
    Task<PlannedTransactionPageResult> ListAsync(
        PlannedTransactionStatus? status, PlannedTransactionCursor? afterCursor, int limit, CancellationToken cancellationToken);

    /// <summary>All plans (active and finished), or the one plan when <paramref name="onlyId"/> is set.</summary>
    Task<IReadOnlyList<PlannedTransaction>> ListAllAsync(PlannedTransactionId? onlyId, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlannedTransaction>> ListByWalletAsync(WalletId walletId, CancellationToken cancellationToken);
}

public interface IPlanningUnitOfWork
{
    /// <summary>
    /// The per-wallet advisory lock shared with Wallets and Operations (BuildingBlocks WalletAdvisoryLock). Lock order
    /// across modules: the wallet first, then rows (Wallets commands lock the wallet and their Planning subscribers
    /// update plan rows), so a command that also locks a plan row takes the wallet lock before it (review v0.4.0, R7).
    /// </summary>
    Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public sealed record PlannedTransactionPageResult(IReadOnlyList<PlannedTransaction> Items, bool HasMore);

/// <summary>Opaque keyset cursor over the creation time of the last returned plan.</summary>
public sealed record PlannedTransactionCursor(DateTimeOffset CreatedAt)
{
    public string Encode() =>
        Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(CreatedAt.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    public static PlannedTransactionCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var text = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return long.TryParse(text, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var ticks)
                && ticks >= DateTimeOffset.MinValue.UtcTicks && ticks <= DateTimeOffset.MaxValue.UtcTicks
                ? new PlannedTransactionCursor(new DateTimeOffset(ticks, TimeSpan.Zero))
                : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
