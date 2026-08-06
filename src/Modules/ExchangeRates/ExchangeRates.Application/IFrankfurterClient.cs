namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Порт клиента сервиса Frankfurter (ADR-0001) — реализация в ExchangeRates.Infrastructure
/// через IHttpClientFactory. Работает с кодами валют (например, "USD"), а не CurrencyId —
/// Frankfurter ничего не знает о внутренних Id справочника валют.
/// </summary>
public interface IFrankfurterClient
{
    /// <summary>Последний курс пары from→to. Бросает FrankfurterClientException при ошибке.</summary>
    Task<decimal> GetLatestRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken);

    /// <summary>
    /// Курс пары from→to на дату (или ближайшую предыдущую торговую дату, которую вернёт
    /// сам Frankfurter — RatesAsOfDate содержит фактически применённую дату). Бросает
    /// FrankfurterClientException при ошибке.
    /// </summary>
    Task<(decimal Rate, DateOnly RatesAsOfDate)> GetHistoricalRateAsync(
        string fromCurrencyCode, string toCurrencyCode, DateOnly date, CancellationToken cancellationToken);
}

public abstract class FrankfurterClientException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Сеть/таймаут/5xx после исчерпания ретраев — временная недоступность источника (ADR-0001 п.6).</summary>
public sealed class FrankfurterUnavailableException(string message, Exception? inner = null) : FrankfurterClientException(message, inner);

/// <summary>Frankfurter не знает такую валюту/пару — постоянное свойство валюты, не временный сбой (ADR-0001, "Последствия").</summary>
public sealed class FrankfurterUnsupportedCurrencyException(string fromCurrencyCode, string toCurrencyCode)
    : FrankfurterClientException($"Frankfurter не поддерживает пару {fromCurrencyCode}->{toCurrencyCode}.");
