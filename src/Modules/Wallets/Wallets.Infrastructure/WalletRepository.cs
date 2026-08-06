using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

public sealed class WalletRepository(WalletsDbContext dbContext) : IWalletRepository
{
    public void Add(Wallet wallet) => dbContext.Wallets.Add(wallet);

    public void Remove(Wallet wallet) => dbContext.Wallets.Remove(wallet);

    public Task<Wallet?> GetByIdAsync(WalletId id, CancellationToken cancellationToken) =>
        dbContext.Wallets.FirstOrDefaultAsync(w => w.Id == id, cancellationToken);

    public Task<bool> AnyExistsAsync(CancellationToken cancellationToken) =>
        dbContext.Wallets.AnyAsync(cancellationToken);

    public Task<Wallet?> GetCurrentPrimaryAsync(CancellationToken cancellationToken) =>
        dbContext.Wallets.FirstOrDefaultAsync(w => w.IsPrimary, cancellationToken);

    public async Task<WalletPageResult> ListAsync(
        bool includeArchived,
        WalletCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.Wallets.AsQueryable();

        if (!includeArchived)
        {
            query = query.Where(w => !w.IsArchived);
        }

        if (afterCursor is not null)
        {
            var cursorDisplayOrder = afterCursor.DisplayOrder;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(w =>
                w.DisplayOrder > cursorDisplayOrder ||
                (w.DisplayOrder == cursorDisplayOrder && w.CreatedAt > cursorCreatedAt));
        }

        var items = await query
            .OrderBy(w => w.DisplayOrder)
            .ThenBy(w => w.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new WalletPageResult(items, hasMore);
    }
}
