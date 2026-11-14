using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

public sealed class HistoricalRateCoverageRepository(ExchangeRatesDbContext dbContext) : IHistoricalRateCoverageRepository
{
    public void Add(HistoricalRateCoverage coverage) => dbContext.HistoricalRateCoverages.Add(coverage);

    public Task<HistoricalRateCoverage?> FindAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken) =>
        dbContext.HistoricalRateCoverages.FirstOrDefaultAsync(
            c => c.FromCurrencyId == from && c.ToCurrencyId == to, cancellationToken);

    public Task RemoveAllForCurrencyAsync(CurrencyId currencyId, CancellationToken cancellationToken) =>
        dbContext.HistoricalRateCoverages
            .Where(c => c.FromCurrencyId == currencyId || c.ToCurrencyId == currencyId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task RemoveStaleAsync(
        CurrencyId primaryCurrencyId, IReadOnlyCollection<CurrencyId> activeCurrencyIds, CancellationToken cancellationToken)
    {
        await dbContext.HistoricalRateCoverages
            .Where(c => c.ToCurrencyId != primaryCurrencyId)
            .ExecuteDeleteAsync(cancellationToken);

        var sourceIds = await dbContext.HistoricalRateCoverages
            .Select(c => c.FromCurrencyId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var sourceId in sourceIds.Where(id => !activeCurrencyIds.Contains(id)))
        {
            await dbContext.HistoricalRateCoverages
                .Where(c => c.FromCurrencyId == sourceId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
