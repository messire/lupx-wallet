namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Port for the Frankfurter service client (ADR-0004) — implemented in
/// ExchangeRates.Infrastructure via IHttpClientFactory. Works with currency codes (e.g.
/// "USD"), not CurrencyId — Frankfurter knows nothing about internal reference-data Ids.
/// </summary>
public interface IFrankfurterClient
{
    /// <summary>Latest from→to rate. Throws FrankfurterClientException on error.</summary>
    Task<decimal> GetLatestRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken);

    /// <summary>
    /// Daily from→to rates for [<paramref name="fromDate"/>; <paramref name="toDate"/>] in one request
    /// (ADR-0004, §9), ordered by date. Days the service has no rate for (weekends, dates before the
    /// currency's data starts) are absent. <c>200 []</c> yields an empty list — the caller decides
    /// what that means for the period; a code the service rejects (422) throws
    /// <see cref="FrankfurterUnsupportedCurrencyException"/>.
    /// </summary>
    Task<IReadOnlyList<(DateOnly Date, decimal Rate)>> GetRatesForPeriodAsync(
        string fromCurrencyCode, string toCurrencyCode, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>
    /// Currencies the service offers (ADR-0004, §7) — the available list. Throws
    /// <see cref="FrankfurterUnavailableException"/> when the body is not the expected array of
    /// objects with a non-empty <c>iso_code</c>.
    /// </summary>
    Task<IReadOnlyList<FrankfurterCurrency>> GetCurrenciesAsync(CancellationToken cancellationToken);
}

/// <summary>A currency offered by Frankfurter: ISO-like code and the service's English name.</summary>
public sealed record FrankfurterCurrency(string Code, string Name);

public abstract class FrankfurterClientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>
/// The source is unusable: network/timeout/5xx after retries are exhausted, a wrong base
/// address (404), or an unparsable/unexpected response body (ADR-0004 p.6).
/// </summary>
public sealed class FrankfurterUnavailableException(string message, Exception? inner = null) : FrankfurterClientException(message, inner);

/// <summary>Frankfurter has no data for this currency/pair/date — a permanent property of the pair, not a transient failure (ADR-0004, "Consequences").</summary>
public sealed class FrankfurterUnsupportedCurrencyException(string fromCurrencyCode, string toCurrencyCode)
    : FrankfurterClientException($"Frankfurter не имеет данных по паре {fromCurrencyCode}->{toCurrencyCode} (на запрошенную дату).");
