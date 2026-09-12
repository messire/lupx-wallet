using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ExchangeRates.Infrastructure;

public sealed class HistoricalExchangeRateRepository(ExchangeRatesDbContext dbContext) : IHistoricalExchangeRateRepository
{
    public void Add(HistoricalExchangeRate rate) => dbContext.HistoricalExchangeRates.Add(rate);

    public Task<HistoricalExchangeRate?> FindAsync(CurrencyId from, CurrencyId to, DateOnly rateDate, CancellationToken cancellationToken) =>
        dbContext.HistoricalExchangeRates.FirstOrDefaultAsync(
            r => r.FromCurrencyId == from && r.ToCurrencyId == to && r.RateDate == rateDate, cancellationToken);

    public Task<HistoricalExchangeRate?> FindClosestBeforeAsync(
        CurrencyId from, CurrencyId to, DateOnly onOrBefore, CancellationToken cancellationToken) =>
        dbContext.HistoricalExchangeRates
            .Where(r => r.FromCurrencyId == from && r.ToCurrencyId == to && r.RateDate <= onOrBefore)
            .OrderByDescending(r => r.RateDate)
            .FirstOrDefaultAsync(cancellationToken);
}
