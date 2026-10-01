using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class AvailableCurrencyRepository(ReferenceDataDbContext dbContext) : IAvailableCurrencyRepository
{
    public Task<AvailableCurrency?> GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var normalized = code.Trim().ToUpperInvariant();
        return dbContext.AvailableCurrencies
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Code == normalized, cancellationToken);
    }

    public async Task<IReadOnlyList<AvailableCurrencyDto>> ListAsync(CancellationToken cancellationToken) =>
        await dbContext.AvailableCurrencies
            .AsNoTracking()
            .OrderBy(x => x.Code)
            .Select(x => new AvailableCurrencyDto(
                x.Code, x.Name, dbContext.Currencies.Any(c => c.Code == x.Code)))
            .ToListAsync(cancellationToken);
}
