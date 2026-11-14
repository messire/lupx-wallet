using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Public read-only contract of the ExchangeRates module for Reporting. Reads the database only —
/// no call to the rate service while data is being read (ADR-0004, §8).
/// </summary>
public interface IExchangeRateLookup
{
    /// <summary>Latest known rate for the pair (UC-08/UC-10). Same currency → 1. null if the pair was never requested.</summary>
    Task<decimal?> GetRateAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken);

    /// <summary>
    /// Rate as of a date (UC-21) from the loaded history: the rate of that date, or the closest
    /// earlier one when the date has none (weekend, holiday) — <c>RatesAsOfDate</c> carries the
    /// date actually applied. Same currency → (1, date). null when the history holds nothing
    /// on or before the date (not loaded yet, ADR-0004, §9).
    /// </summary>
    Task<(decimal Rate, DateOnly RatesAsOfDate)?> GetHistoricalRateAsync(
        CurrencyId from, CurrencyId to, DateOnly date, CancellationToken cancellationToken);
}
