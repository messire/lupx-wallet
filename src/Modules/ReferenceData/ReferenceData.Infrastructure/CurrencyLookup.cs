using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class CurrencyLookup(ReferenceDataDbContext dbContext) : ICurrencyLookup
{
    public async Task<CurrencyLookupResult?> GetAsync(CurrencyId id, CancellationToken cancellationToken)
    {
        var currency = await dbContext.Currencies.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return currency is null ? null : new CurrencyLookupResult(currency.Id, currency.Code, currency.IsActive);
    }

    public async Task<IReadOnlyList<CurrencyLookupResult>> GetActiveAsync(CancellationToken cancellationToken) =>
        await dbContext.Currencies
            .Where(x => x.IsActive)
            .Select(x => new CurrencyLookupResult(x.Id, x.Code, x.IsActive))
            .ToListAsync(cancellationToken);
}
