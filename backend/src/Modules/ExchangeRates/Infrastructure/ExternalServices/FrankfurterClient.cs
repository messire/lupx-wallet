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
/// v2 contract: <c>GET rates?base=X&amp;quotes=Y[&amp;from=D1&amp;to=D2]</c> returns a JSON array
/// <c>[{date, base, quote, rate}]</c> — one element without a period (the latest rate), a daily
/// series with one. A currency without data in the range yields <c>200 []</c>; an unknown code
/// yields <c>422</c> (<see cref="FrankfurterUnsupportedCurrencyException"/>). For the latest rate an
/// empty array is the same permanent "no data" signal; for a period it is returned as an empty list.
/// <c>GET currencies</c> returns <c>[{iso_code, name, ...}]</c>.
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
        return ParseQuote(document, fromCurrencyCode, toCurrencyCode);
    }

    public async Task<IReadOnlyList<(DateOnly Date, decimal Rate)>> GetRatesForPeriodAsync(
        string fromCurrencyCode, string toCurrencyCode, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken)
    {
        var relativeUrl = $"rates?base={Uri.EscapeDataString(fromCurrencyCode)}&quotes={Uri.EscapeDataString(toCurrencyCode)}"
            + $"&from={fromDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&to={toDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
        using var document = await SendWithRetryAsync(relativeUrl, fromCurrencyCode, toCurrencyCode, cancellationToken);
        return ParsePeriod(document, toCurrencyCode);
    }

    public async Task<IReadOnlyList<FrankfurterCurrency>> GetCurrenciesAsync(CancellationToken cancellationToken)
    {
        const string relativeUrl = "currencies";
        JsonDocument document;
        try
        {
            document = await SendWithRetryAsync(relativeUrl, relativeUrl, relativeUrl, cancellationToken);
        }
        catch (FrankfurterUnsupportedCurrencyException ex)
        {
            throw new FrankfurterUnavailableException("Frankfurter вернул 422 для списка валют.", ex);
        }

        using (document)
        {
            return ParseCurrencies(document);
        }
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
    private static decimal ParseQuote(JsonDocument document, string fromCode, string toCode)
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

            return rate;
        }

        throw new FrankfurterUnsupportedCurrencyException(fromCode, toCode);
    }

    /// <summary>
    /// Reads the from→to series of a period response. An empty array is a valid answer (no data in
    /// the period). Elements of other quotes are skipped; an element without a valid date or with a
    /// missing/non-numeric/non-positive rate is a malformed response (<see cref="FrankfurterUnavailableException"/>).
    /// </summary>
    private static IReadOnlyList<(DateOnly Date, decimal Rate)> ParsePeriod(JsonDocument document, string toCode)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FrankfurterUnavailableException("Frankfurter вернул ответ неожиданного формата (ожидался массив).");
        }

        var rates = new List<(DateOnly Date, decimal Rate)>();
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("quote", out var quoteElement)
                || quoteElement.ValueKind != JsonValueKind.String
                || !string.Equals(quoteElement.GetString(), toCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!element.TryGetProperty("date", out var dateElement)
                || dateElement.ValueKind != JsonValueKind.String
                || !DateOnly.TryParseExact(dateElement.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                throw new FrankfurterUnavailableException("Frankfurter вернул элемент ряда курсов без корректной даты.");
            }

            if (!element.TryGetProperty("rate", out var rateElement)
                || rateElement.ValueKind != JsonValueKind.Number
                || !rateElement.TryGetDecimal(out var rate)
                || rate <= 0)
            {
                throw new FrankfurterUnavailableException($"Frankfurter вернул некорректное значение rate на {date:yyyy-MM-dd}.");
            }

            rates.Add((date, rate));
        }

        rates.Sort((a, b) => a.Date.CompareTo(b.Date));
        return rates;
    }

    /// <summary>
    /// Reads <c>[{iso_code, name, ...}]</c>. A body that is not an array of objects with a non-empty
    /// <c>iso_code</c> is a source failure; a missing name falls back to the code. Duplicate codes keep the first.
    /// </summary>
    private static IReadOnlyList<FrankfurterCurrency> ParseCurrencies(JsonDocument document)
    {
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new FrankfurterUnavailableException("Frankfurter вернул список валют неожиданного формата (ожидался массив).");
        }

        var currencies = new List<FrankfurterCurrency>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object
                || !element.TryGetProperty("iso_code", out var codeElement)
                || codeElement.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(codeElement.GetString()))
            {
                throw new FrankfurterUnavailableException("Frankfurter вернул элемент списка валют без iso_code.");
            }

            var code = codeElement.GetString()!.Trim();
            var name = element.TryGetProperty("name", out var nameElement) && nameElement.ValueKind == JsonValueKind.String
                && !string.IsNullOrWhiteSpace(nameElement.GetString())
                    ? nameElement.GetString()!.Trim()
                    : code;

            if (seen.Add(code))
            {
                currencies.Add(new FrankfurterCurrency(code, name));
            }
        }

        return currencies;
    }

    /// <summary>Retryable non-success status (5xx and the like); kept private so the retry filter cannot catch deterministic failures.</summary>
    private sealed class TransientResponseException(string message) : Exception(message);
}
