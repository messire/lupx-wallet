namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Port for the Frankfurter service client (ADR-0001) — implemented in
/// ExchangeRates.Infrastructure via IHttpClientFactory. Works with currency codes (e.g.
/// "USD"), not CurrencyId — Frankfurter knows nothing about internal reference-data Ids.
/// </summary>
public interface IFrankfurterClient
{
    /// <summary>Latest from→to rate. Throws FrankfurterClientException on error.</summary>
    Task<decimal> GetLatestRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken);

    /// <summary>
    /// from→to rate on a date (or the closest earlier trading date Frankfurter itself
    /// returns — RatesAsOfDate carries the actually applied date). Throws
    /// FrankfurterClientException on error.
    /// </summary>
    Task<(decimal Rate, DateOnly RatesAsOfDate)> GetHistoricalRateAsync(
        string fromCurrencyCode, string toCurrencyCode, DateOnly date, CancellationToken cancellationToken);
}

public abstract class FrankfurterClientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Network/timeout/5xx after retries are exhausted — a temporary source outage (ADR-0001 p.6).</summary>
public sealed class FrankfurterUnavailableException(string message, Exception? inner = null) : FrankfurterClientException(message, inner);

/// <summary>Frankfurter does not know this currency/pair — a permanent currency property, not a transient failure (ADR-0001, "Consequences").</summary>
public sealed class FrankfurterUnsupportedCurrencyException(string fromCurrencyCode, string toCurrencyCode)
    : FrankfurterClientException($"Frankfurter не поддерживает пару {fromCurrencyCode}->{toCurrencyCode}.");
