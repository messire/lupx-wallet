using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

public interface IHistoricalExchangeRateRepository
{
    void Add(HistoricalExchangeRate rate);

    Task<HistoricalExchangeRate?> FindAsync(CurrencyId from, CurrencyId to, DateOnly rateDate, CancellationToken cancellationToken);

    /// <summary>
    /// Closest cached date not after <paramref name="onOrBefore"/> (UC-21 fallback, for when
    /// Frankfurter/the source itself also has no data for the needed date).
    /// </summary>
    Task<HistoricalExchangeRate?> FindClosestBeforeAsync(
        CurrencyId from, CurrencyId to, DateOnly onOrBefore, CancellationToken cancellationToken);
}
