using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class WalletTypeRepository(ReferenceDataDbContext dbContext) : IWalletTypeRepository
{
    public void Add(WalletType walletType) => dbContext.WalletTypes.Add(walletType);

    public void Remove(WalletType walletType) => dbContext.WalletTypes.Remove(walletType);

    public Task<WalletType?> GetByIdAsync(WalletTypeId id, CancellationToken cancellationToken) =>
        dbContext.WalletTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<WalletTypePageResult> ListAsync(
        bool includeInactive, NameCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.WalletTypes.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (afterCursor is not null)
        {
            var cursorName = afterCursor.Name;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(x =>
                string.Compare(x.Name, cursorName) > 0 ||
                (x.Name == cursorName && x.CreatedAt > cursorCreatedAt));
        }

        var items = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new WalletTypePageResult(items, hasMore);
    }
}
