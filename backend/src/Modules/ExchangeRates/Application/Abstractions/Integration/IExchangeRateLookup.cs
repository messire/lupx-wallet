using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Public read-only contract of the ExchangeRates module for future consumers (Reporting,
/// wave 2, W2.1) — declared ahead of time per W1.1 (docs/PROGRESS.md), so
/// Reporting.Application can reference it without changes to ExchangeRates.
/// </summary>
public interface IExchangeRateLookup
{
    /// <summary>Latest known rate for the pair (UC-08/UC-10). Same currency → 1. null if the pair was never requested.</summary>
    Task<decimal?> GetRateAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken);

    /// <summary>
    /// Rate as of a date (UC-21) — when not cached, requests Frankfurter and stores the
    /// result (append-only, historical_exchange_rates); when the source is unavailable or has
    /// no data for the exact date, returns the closest previously cached rate with its actual
    /// date (RatesAsOfDate) — requirements §5. null if there is no data at all.
    /// </summary>
    Task<(decimal Rate, DateOnly RatesAsOfDate)?> GetOrFetchHistoricalRateAsync(
        CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken);
}
