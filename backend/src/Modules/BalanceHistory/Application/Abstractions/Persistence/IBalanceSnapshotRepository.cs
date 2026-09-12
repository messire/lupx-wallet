using LupexWallet.BalanceHistory.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// Repository port (interface declared in Application, implementation in Infrastructure).
/// BalanceSnapshot identity is the pair (WalletId, SnapshotDate), see BalanceSnapshot.
/// </summary>
public interface IBalanceSnapshotRepository
{
    void Add(BalanceSnapshot snapshot);

    Task<BalanceSnapshot?> GetAsync(WalletId walletId, DateOnly snapshotDate, CancellationToken cancellationToken);

    /// <summary>All existing wallet snapshots in the [from, to] range — one query instead of N (used by cascading recalculation, ADR-0003).</summary>
    Task<IReadOnlyList<BalanceSnapshot>> GetRangeAsync(WalletId walletId, DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>The latest (maximum) existing snapshot date for the wallet, if any — used by the backfill job (ADR-0004).</summary>
    Task<DateOnly?> GetLatestSnapshotDateAsync(WalletId walletId, CancellationToken cancellationToken);

    /// <summary>A page of wallet snapshots for the period [from, to], ordered by ascending date (docs/api/openapi.yaml: GET /wallets/{id}/balance-history).</summary>
    Task<BalanceSnapshotPageResult> ListAsync(
        WalletId walletId,
        DateOnly from,
        DateOnly to,
        BalanceSnapshotCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken);
}

/// <summary>
/// A cursor over SnapshotDate only — unlike WalletCursor/OperationCursor in other modules,
/// no tiebreaker such as CreatedAt is needed here: (WalletId, SnapshotDate) is already the
/// composite primary key of BalanceSnapshot (schema.md), so within a single wallet
/// SnapshotDate alone is unique and monotonically orders pages.
/// </summary>
public sealed record BalanceSnapshotCursor(DateOnly SnapshotDate)
{
    public string Encode() => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes($"{SnapshotDate:O}"));

    public static BalanceSnapshotCursor? TryDecode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var raw = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            return new BalanceSnapshotCursor(DateOnly.Parse(raw));
        }
        catch
        {
            return null;
        }
    }
}

public sealed record BalanceSnapshotPageResult(IReadOnlyList<BalanceSnapshot> Items, bool HasMore);
