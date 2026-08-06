using System.Globalization;
using System.Text.Json;
using LupexWallet.ExchangeRates.Application;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Клиент сервиса Frankfurter (ADR-0001) через IHttpClientFactory — таймаут настроен на
/// именованном HttpClient (см. ExchangeRatesModuleExtensions), здесь — до 3 попыток с
/// нарастающей паузой при сетевой ошибке/таймауте/5xx (в решении нет пакета Polly —
/// ретраи реализованы вручную, аналогично отсутствию внешних retry-библиотек в остальных
/// модулях проекта). Работает с кодами валют (ISO-подобные строки), не CurrencyId.
/// </summary>
public sealed class FrankfurterClient(IHttpClientFactory httpClientFactory, ILogger<FrankfurterClient> logger) : IFrankfurterClient
{
    public const string HttpClientName = "Frankfurter";
    private const int MaxAttempts = 3;

    public async Task<decimal> GetLatestRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken)
    {
        var relativeUrl = $"latest?base={Uri.EscapeDataString(fromCurrencyCode)}&symbols={Uri.EscapeDataString(toCurrencyCode)}";
        using var document = await SendWithRetryAsync(relativeUrl, cancellationToken);
        return ParseRate(document, fromCurrencyCode, toCurrencyCode);
    }

    public async Task<(decimal Rate, DateOnly RatesAsOfDate)> GetHistoricalRateAsync(
        string fromCurrencyCode, string toCurrencyCode, DateOnly date, CancellationToken cancellationToken)
    {
        var datePart = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var relativeUrl = $"{datePart}?base={Uri.EscapeDataString(fromCurrencyCode)}&symbols={Uri.EscapeDataString(toCurrencyCode)}";
        using var document = await SendWithRetryAsync(relativeUrl, cancellationToken);

        var rate = ParseRate(document, fromCurrencyCode, toCurrencyCode);
        var actualDate = document.RootElement.TryGetProperty("date", out var dateElement)
            && DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed)
                ? parsed
                : date;

        return (rate, actualDate);
    }

    private async Task<JsonDocument> SendWithRetryAsync(string relativeUrl, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(relativeUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    throw new FrankfurterUnavailableException($"Frankfurter вернул {(int)response.StatusCode} для {relativeUrl}.");
                }

                var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or FrankfurterUnavailableException)
            {
                lastError = ex;
                logger.LogWarning(
                    ex, "Попытка {Attempt}/{Max} обращения к Frankfurter ({Url}) неуспешна", attempt, MaxAttempts, relativeUrl);

                if (attempt < MaxAttempts)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(200 * attempt), cancellationToken);
                }
            }
        }

        throw new FrankfurterUnavailableException(
            $"Frankfurter недоступен после {MaxAttempts} попыток ({relativeUrl}).", lastError);
    }

    private static decimal ParseRate(JsonDocument document, string fromCode, string toCode)
    {
        if (!document.RootElement.TryGetProperty("rates", out var rates) || !rates.TryGetProperty(toCode, out var rateElement))
        {
            throw new FrankfurterUnsupportedCurrencyException(fromCode, toCode);
        }

        return rateElement.GetDecimal();
    }
}
