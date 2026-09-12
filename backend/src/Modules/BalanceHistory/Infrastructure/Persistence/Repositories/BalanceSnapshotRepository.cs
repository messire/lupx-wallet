using LupexWallet.BalanceHistory.Application;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.BalanceHistory.Infrastructure;

public sealed class BalanceSnapshotRepository(BalanceHistoryDbContext dbContext) : IBalanceSnapshotRepository
{
    public void Add(BalanceSnapshot snapshot) => dbContext.BalanceSnapshots.Add(snapshot);

    public Task<BalanceSnapshot?> GetAsync(WalletId walletId, DateOnly snapshotDate, CancellationToken cancellationToken) =>
        dbContext.BalanceSnapshots.FirstOrDefaultAsync(
            x => x.WalletId == walletId && x.SnapshotDate == snapshotDate, cancellationToken);

    public async Task<IReadOnlyList<BalanceSnapshot>> GetRangeAsync(
        WalletId walletId, DateOnly from, DateOnly to, CancellationToken cancellationToken) =>
        await dbContext.BalanceSnapshots
            .Where(x => x.WalletId == walletId && x.SnapshotDate >= from && x.SnapshotDate <= to)
            .ToListAsync(cancellationToken);

    public async Task<DateOnly?> GetLatestSnapshotDateAsync(WalletId walletId, CancellationToken cancellationToken)
    {
        var latest = await dbContext.BalanceSnapshots
            .Where(x => x.WalletId == walletId)
            .OrderByDescending(x => x.SnapshotDate)
            .Select(x => (DateOnly?)x.SnapshotDate)
            .FirstOrDefaultAsync(cancellationToken);

        return latest;
    }

    public async Task<BalanceSnapshotPageResult> ListAsync(
        WalletId walletId,
        DateOnly from,
        DateOnly to,
        BalanceSnapshotCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.BalanceSnapshots
            .Where(x => x.WalletId == walletId && x.SnapshotDate >= from && x.SnapshotDate <= to);

        if (afterCursor is not null)
        {
            var cursorDate = afterCursor.SnapshotDate;
            query = query.Where(x => x.SnapshotDate > cursorDate);
        }

        var items = await query
            .OrderBy(x => x.SnapshotDate)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new BalanceSnapshotPageResult(items, hasMore);
    }
}
