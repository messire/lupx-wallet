using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

public interface IHistoricalExchangeRateRepository
{
    void Add(HistoricalExchangeRate rate);

    Task<HistoricalExchangeRate?> FindAsync(CurrencyId from, CurrencyId to, DateOnly rateDate, CancellationToken cancellationToken);

    /// <summary>
    /// Ближайшая закэшированная дата не позже <paramref name="onOrBefore"/> (UC-21 fallback,
    /// если у самого Frankfurter/источника тоже нет данных на нужную дату).
    /// </summary>
    Task<HistoricalExchangeRate?> FindClosestBeforeAsync(
        CurrencyId from, CurrencyId to, DateOnly onOrBefore, CancellationToken cancellationToken);
}
