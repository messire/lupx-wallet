using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Публичный read-only контракт модуля ExchangeRates для будущих потребителей (Reporting,
/// волна 2, W2.1) — объявлен здесь заранее по требованию W1.1 (docs/PROGRESS.md), чтобы
/// Reporting.Application мог сослаться на него без изменений в ExchangeRates.
/// </summary>
public interface IExchangeRateLookup
{
    /// <summary>Последний известный курс пары (UC-08/UC-10). Одинаковые валюты → 1. null, если пара ещё не запрашивалась.</summary>
    Task<decimal?> GetRateAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken);

    /// <summary>
    /// Курс на дату (UC-21) — при отсутствии в кэше запрашивает Frankfurter и сохраняет
    /// (append-only, historical_exchange_rates), при недоступности источника или отсутствии
    /// данных на точную дату возвращает ближайший ранее закэшированный курс с его фактической
    /// датой (RatesAsOfDate) — раздел 5 требований. null, если данных нет вовсе ни на одну дату.
    /// </summary>
    Task<(decimal Rate, DateOnly RatesAsOfDate)?> GetOrFetchHistoricalRateAsync(
        CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken);
}
