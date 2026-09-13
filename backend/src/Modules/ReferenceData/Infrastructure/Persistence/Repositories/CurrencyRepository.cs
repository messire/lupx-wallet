using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class CurrencyRepository(ReferenceDataDbContext dbContext) : ICurrencyRepository
{
    public void Add(Currency currency) => dbContext.Currencies.Add(currency);

    public void Remove(Currency currency) => dbContext.Currencies.Remove(currency);

    public Task<Currency?> GetByIdAsync(CurrencyId id, CancellationToken cancellationToken) =>
        dbContext.Currencies.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> CodeExistsAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return dbContext.Currencies.AnyAsync(x => x.Code == normalized, cancellationToken);
    }

    public async Task<CurrencyPageResult> ListAsync(
        bool includeInactive, NameCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Currencies.AsQueryable();

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

        return new CurrencyPageResult(items, hasMore);
    }
}
