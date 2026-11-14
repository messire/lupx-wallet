using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

public sealed class HistoricalExchangeRateRepository(ExchangeRatesDbContext dbContext) : IHistoricalExchangeRateRepository
{
    public void Add(HistoricalExchangeRate rate) => dbContext.HistoricalExchangeRates.Add(rate);

    public async Task<IReadOnlyList<HistoricalExchangeRate>> GetRangeAsync(
        CurrencyId from, CurrencyId to, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
        await dbContext.HistoricalExchangeRates
            .Where(r => r.FromCurrencyId == from && r.ToCurrencyId == to && r.RateDate >= fromDate && r.RateDate <= toDate)
            .ToListAsync(cancellationToken);

    public Task<HistoricalExchangeRate?> FindClosestBeforeAsync(
        CurrencyId from, CurrencyId to, DateOnly onOrBefore, CancellationToken cancellationToken) =>
        dbContext.HistoricalExchangeRates
            .AsNoTracking()
            .Where(r => r.FromCurrencyId == from && r.ToCurrencyId == to && r.RateDate <= onOrBefore)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(cancellationToken);

    public Task RemoveAllForCurrencyAsync(CurrencyId currencyId, CancellationToken cancellationToken) =>
        dbContext.HistoricalExchangeRates
            .Where(r => r.FromCurrencyId == currencyId || r.ToCurrencyId == currencyId)
            .ExecuteDeleteAsync(cancellationToken);

    public async Task RemoveStaleAsync(
        CurrencyId primaryCurrencyId, IReadOnlyCollection<CurrencyId> activeCurrencyIds, CancellationToken cancellationToken)
    {
        await dbContext.HistoricalExchangeRates
            .Where(r => r.ToCurrencyId != primaryCurrencyId)
            .ExecuteDeleteAsync(cancellationToken);

        // The source currencies are few; filtering them in memory avoids translating a list of
        // value-converted ids into SQL.
        var sourceIds = await dbContext.HistoricalExchangeRates
            .Select(r => r.FromCurrencyId)
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var sourceId in sourceIds.Where(id => !activeCurrencyIds.Contains(id)))
        {
            await dbContext.HistoricalExchangeRates
                .Where(r => r.FromCurrencyId == sourceId)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }
}
