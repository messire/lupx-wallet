using System.Globalization;
using System.Net;
using System.Text.Json;
using LupexWallet.ExchangeRates.Application;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Frankfurter API v2 client (ADR-0004, p.1) via IHttpClientFactory — the timeout and base
/// address (<c>https://api.frankfurter.dev/v2/</c>, trailing slash required) are configured on
/// the named HttpClient (see ExchangeRatesModuleExtensions); here — up to 3 attempts with an
/// increasing delay on network error/timeout/5xx (the solution has no Polly package — retries
/// are hand-rolled, matching the absence of external retry libraries in the rest of the
/// project's modules). Works with currency codes (ISO-like strings), not CurrencyId.
/// <para>
/// v2 contract: <c>GET rates?base=X&amp;quotes=Y[&amp;date=D]</c> returns a JSON array
/// <c>[{date, base, quote, rate}]</c>. A currency without data or a date before the start of
/// the data yields <c>200 []</c>; a non-trading date yields the closest earlier date in the
/// element's <c>date</c> field; an unknown code yields <c>422</c>. "Empty array" and 422 are a
/// permanent property of the pair (<see cref="FrankfurterUnsupportedCurrencyException"/>).
/// </para>
/// <para>
/// 404 is not a property of the pair: it only comes from a wrong base address. A malformed
/// body (non-JSON, wrong shape, non-numeric rate) is likewise a source failure. All of these
/// are <see cref="FrankfurterUnavailableException"/> without retries (deterministic, a retry
/// cannot help) so the refresh raises ExchangeRateUpdateFailed instead of freezing silently.
/// Anything else thrown here derives from <see cref="FrankfurterClientException"/>; caller
/// cancellation propagates as <see cref="OperationCanceledException"/>.
/// </para>
/// </summary>
public sealed class FrankfurterClient(IHttpClientFactory httpClientFactory, ILogger<FrankfurterClient> logger) : IFrankfurterClient
{
    public const string HttpClientName = "Frankfurter";
    private const int MaxAttempts = 3;

    public async Task<decimal> GetLatestRateAsync(string fromCurrencyCode, string toCurrencyCode, CancellationToken cancellationToken)
    {
        var relativeUrl = $"rates?base={Uri.EscapeDataString(fromCurrencyCode)}&quotes={Uri.EscapeDataString(toCurrencyCode)}";
        using var document = await SendWithRetryAsync(relativeUrl, fromCurrencyCode, toCurrencyCode, cancellationToken);
        return ParseQuote(document, fromCurrencyCode, toCurrencyCode).Rate;
    }

    public async Task<(decimal Rate, DateOnly RatesAsOfDate)> GetHistoricalRateAsync(
        string fromCurrencyCode, string toCurrencyCode, DateOnly date, CancellationToken cancellationToken)
    {
        var datePart = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var relativeUrl = $"rates?base={Uri.EscapeDataString(fromCurrencyCode)}&quotes={Uri.EscapeDataString(toCurrencyCode)}&date={datePart}";
        using var document = await SendWithRetryAsync(relativeUrl, fromCurrencyCode, toCurrencyCode, cancellationToken);

        var quote = ParseQuote(document, fromCurrencyCode, toCurrencyCode);
        return (quote.Rate, quote.Date ?? date);
    }

    private async Task<JsonDocument> SendWithRetryAsync(
        string relativeUrl, string fromCode, string toCode, CancellationToken cancellationToken)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        Exception? lastError = null;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                using var response = await client.GetAsync(relativeUrl, cancellationToken);

                // 422 — a code the service does not recognise: a permanent property of the pair.
                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    throw new FrankfurterUnsupportedCurrencyException(fromCode, toCode);
                }

                // 404 means a wrong base address (config error) — fail fast, no retries.
                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    throw new FrankfurterUnavailableException(
                        $"Frankfurter вернул 404 для {relativeUrl} — проверьте ExchangeRates:FrankfurterBaseUrl (ожидается .../v2/).");
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new TransientResponseException($"Frankfurter вернул {(int)response.StatusCode} для {relativeUrl}.");
                }

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                try
                {
                    return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                }
                catch (JsonException ex)
                {
                    // Non-JSON body (e.g. an HTML page from a CDN) — deterministic, no retries.
                    throw new FrankfurterUnavailableException($"Frankfurter вернул не-JSON ответ для {relativeUrl}.", ex);
                }
            }
            catch (Exception ex) when (
                ex is HttpRequestException or IOException or TransientResponseException
                || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
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

    /// <summary>
    /// Picks the from→to element of the v2 response array. An empty array (or no matching
    /// element) means the pair has no data — <see cref="FrankfurterUnsupportedCurrencyException"/>.
    /// A matching element with a missing/non-numeric/out-of-range rate is a malformed response —
    /// <see cref="FrankfurterUnavailableException"/>. Codes are compared case-insensitively
    /// (the service normalises them to upper case).
    /// </summary>
    private static (decimal Rate, DateOnly? Date) ParseQuote(JsonDocument document, string fromCode, string toCode)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FrankfurterUnavailableException("Frankfurter вернул ответ неожиданного формата (ожидался массив).");
        }

        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("quote", out var quoteElement)
                || quoteElement.ValueKind != JsonValueKind.String
                || !string.Equals(quoteElement.GetString(), toCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!element.TryGetProperty("rate", out var rateElement)
                || rateElement.ValueKind != JsonValueKind.Number
                || !rateElement.TryGetDecimal(out var rate))
            {
                throw new FrankfurterUnavailableException(
                    $"Frankfurter вернул некорректное значение rate для пары {fromCode}->{toCode}.");
            }

            DateOnly? date = element.TryGetProperty("date", out var dateElement)
                && dateElement.ValueKind == JsonValueKind.String
                && DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
                    ? parsed
                    : null;

            return (rate, date);
        }

        throw new FrankfurterUnsupportedCurrencyException(fromCode, toCode);
    }

    /// <summary>Retryable non-success status (5xx and the like); kept private so the retry filter cannot catch deterministic failures.</summary>
    private sealed class TransientResponseException(string message) : Exception(message);
}
