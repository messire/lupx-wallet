using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class CurrencyLookup(ReferenceDataDbContext dbContext) : ICurrencyLookup
{
    public async Task<CurrencyLookupResult?> GetAsync(CurrencyId id, CancellationToken cancellationToken) =>
        await dbContext.Currencies
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new CurrencyLookupResult(x.Id, x.Code))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<IReadOnlyList<CurrencyLookupResult>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.Currencies
            .AsNoTracking()
            .Select(x => new CurrencyLookupResult(x.Id, x.Code))
            .ToListAsync(cancellationToken);
}
