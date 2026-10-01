using System.Globalization;
using System.Net;
using System.Text;
using LupexWallet.ExchangeRates.Infrastructure;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>How the fake answers a request instead of a normal response.</summary>
public enum FrankfurterFakeFailure
{
    None,

    /// <summary>503 — the source is unavailable (the client retries, then gives up).</summary>
    ServerError,

    /// <summary>422 — the service does not recognise a code.</summary>
    Unprocessable,

    /// <summary>200 with an empty array — no data (or the pair is unsupported).</summary>
    EmptyArray,
}

/// <summary>A <c>rates</c> request the fake has seen; From/To are null for the "latest rate" form.</summary>
public sealed record FrankfurterRatesRequest(string Base, string Quote, DateOnly? From, DateOnly? To);

/// <summary>
/// Stand-in for the Frankfurter v2 service, installed as <see cref="FrankfurterTestableHandler.TestOverride"/>
/// (no network). Routes by path: <c>currencies</c> → the configured catalog; <c>rates</c> with
/// <c>from</c>/<c>to</c> → a daily series without weekends (like the ECB-based history), <c>rates</c>
/// without a period → the latest rate (the last weekday not after today). Counts calls and can fail
/// per endpoint or per source currency. Creating an instance replaces the override and starts from
/// the defaults, so every test gets a clean fake (the handler is a singleton for the whole run).
/// </summary>
public sealed class FrankfurterFake
{
    private static readonly (string Code, string Name)[] DefaultCatalog =
    [
        ("EUR", "Euro"),
        ("USD", "United States Dollar"),
        ("GBP", "British Pound"),
        ("JPY", "Japanese Yen"),
        ("CHF", "Swiss Franc"),
        ("BTC", "Test Coin"),
    ];

    private readonly object _gate = new();
    private readonly List<Uri> _requests = [];
    private readonly List<FrankfurterRatesRequest> _ratesRequests = [];
    private readonly Dictionary<(string Base, string Quote), decimal> _pairRates = [];
    private readonly Dictionary<string, DateOnly> _dataStart = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, FrankfurterFakeFailure> _ratesFailureByBase = new(StringComparer.OrdinalIgnoreCase);
    private List<(string Code, string Name)> _catalog = [.. DefaultCatalog];
    private int _currenciesCalls;
    private int _ratesCalls;

    public FrankfurterFake(FrankfurterTestableHandler handler)
    {
        handler.TestOverride = HandleAsync;
    }

    /// <summary>Rate returned for a pair that has no explicit <see cref="SetRate"/>.</summary>
    public decimal DefaultRate { get; set; } = 0.9m;

    /// <summary>
    /// Overrides the rate of a date: (base, quote, date) → rate. Lets a test make the rate differ per
    /// day (e.g. to see which day an upsert or a "closest earlier" lookup picked).
    /// </summary>
    public Func<string, string, DateOnly, decimal>? RateForDate { get; set; }

    /// <summary>Failure mode of <c>GET currencies</c>.</summary>
    public FrankfurterFakeFailure CurrenciesFailure { get; set; }

    /// <summary>Failure mode of every <c>GET rates</c> (latest and period).</summary>
    public FrankfurterFakeFailure RatesFailure { get; set; }

    public int CurrenciesCalls { get { lock (_gate) { return _currenciesCalls; } } }

    public int RatesCalls { get { lock (_gate) { return _ratesCalls; } } }

    public int TotalCalls { get { lock (_gate) { return _currenciesCalls + _ratesCalls; } } }

    /// <summary>Every request seen since creation or the last <see cref="ResetCounters"/>.</summary>
    public IReadOnlyList<Uri> Requests { get { lock (_gate) { return [.. _requests]; } } }

    /// <summary>Parsed <c>rates</c> requests (the period form has From/To) since creation or the last <see cref="ResetCounters"/>.</summary>
    public IReadOnlyList<FrankfurterRatesRequest> RatesRequests { get { lock (_gate) { return [.. _ratesRequests]; } } }

    public void ResetCounters()
    {
        lock (_gate)
        {
            _currenciesCalls = 0;
            _ratesCalls = 0;
            _requests.Clear();
            _ratesRequests.Clear();
        }
    }

    /// <summary>Replaces the catalog returned by <c>GET currencies</c>.</summary>
    public void SetCatalog(params (string Code, string Name)[] currencies)
    {
        lock (_gate)
        {
            _catalog = [.. currencies];
        }
    }

    /// <summary>Constant rate for one pair (every day of a series).</summary>
    public void SetRate(string baseCode, string quoteCode, decimal rate)
    {
        lock (_gate)
        {
            _pairRates[(baseCode.ToUpperInvariant(), quoteCode.ToUpperInvariant())] = rate;
        }
    }

    /// <summary>The service has no data for <paramref name="baseCode"/> before <paramref name="firstDate"/>.</summary>
    public void SetDataStart(string baseCode, DateOnly firstDate)
    {
        lock (_gate)
        {
            _dataStart[baseCode] = firstDate;
        }
    }

    /// <summary>Failure mode of <c>GET rates</c> for one source currency only.</summary>
    public void SetRatesFailureFor(string baseCode, FrankfurterFakeFailure failure)
    {
        lock (_gate)
        {
            _ratesFailureByBase[baseCode] = failure;
        }
    }

    private Task<HttpResponseMessage> HandleAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var uri = request.RequestUri!;
        var path = uri.AbsolutePath.Trim('/');
        var endpoint = path.Split('/').Last();

        lock (_gate)
        {
            _requests.Add(uri);

            return Task.FromResult(endpoint switch
            {
                "currencies" => HandleCurrencies(),
                "rates" => HandleRates(uri),
                _ => Respond(HttpStatusCode.NotFound, """{"message":"not found"}"""),
            });
        }
    }

    private HttpResponseMessage HandleCurrencies()
    {
        _currenciesCalls++;

        if (CurrenciesFailure != FrankfurterFakeFailure.None)
        {
            return Failure(CurrenciesFailure);
        }

        var json = "[" + string.Join(",", _catalog.Select(c =>
            $"{{\"iso_code\":\"{c.Code}\",\"iso_numeric\":\"000\",\"name\":\"{c.Name}\",\"symbol\":\"{c.Code}\",\"start_date\":\"1999-01-04\",\"end_date\":\"{Today:yyyy-MM-dd}\"}}")) + "]";
        return Respond(HttpStatusCode.OK, json);
    }

    private HttpResponseMessage HandleRates(Uri uri)
    {
        _ratesCalls++;

        var baseCode = (Query(uri, "base") ?? string.Empty).ToUpperInvariant();
        var quote = (Query(uri, "quotes") ?? string.Empty).ToUpperInvariant();
        var from = ParseDate(Query(uri, "from"));
        var to = ParseDate(Query(uri, "to"));
        _ratesRequests.Add(new FrankfurterRatesRequest(baseCode, quote, from, to));

        var failure = _ratesFailureByBase.TryGetValue(baseCode, out var perBase) && perBase != FrankfurterFakeFailure.None
            ? perBase
            : RatesFailure;
        if (failure != FrankfurterFakeFailure.None)
        {
            return Failure(failure);
        }

        if (baseCode.Length == 0 || quote.Length == 0)
        {
            return Respond(HttpStatusCode.UnprocessableEntity, """{"status":422,"message":"missing base or quotes"}""");
        }

        var dataStart = _dataStart.TryGetValue(baseCode, out var start) ? start : DateOnly.MinValue;
        var elements = new List<string>();
        if (from is { } periodFrom && to is { } periodTo)
        {
            for (var date = periodFrom < dataStart ? dataStart : periodFrom; date <= periodTo && date <= Today; date = date.AddDays(1))
            {
                if (IsWeekend(date))
                {
                    continue;
                }

                elements.Add(Element(date, baseCode, quote));
            }
        }
        else
        {
            // The latest rate: the last weekday not after today (never before the data start).
            var date = Today;
            while (IsWeekend(date))
            {
                date = date.AddDays(-1);
            }

            if (date >= dataStart)
            {
                elements.Add(Element(date, baseCode, quote));
            }
        }

        return Respond(HttpStatusCode.OK, "[" + string.Join(",", elements) + "]");
    }

    private string Element(DateOnly date, string baseCode, string quote)
    {
        var rate = RateForDate?.Invoke(baseCode, quote, date)
            ?? (_pairRates.TryGetValue((baseCode, quote), out var pairRate) ? pairRate : DefaultRate);
        return $"{{\"date\":\"{date:yyyy-MM-dd}\",\"base\":\"{baseCode}\",\"quote\":\"{quote}\",\"rate\":{rate.ToString(CultureInfo.InvariantCulture)}}}";
    }

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private static bool IsWeekend(DateOnly date) => date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;

    private static DateOnly? ParseDate(string? value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ? date : null;

    private static string? Query(Uri uri, string name)
    {
        foreach (var pair in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (Uri.UnescapeDataString(parts[0]) == name)
            {
                return Uri.UnescapeDataString(parts.Length > 1 ? parts[1] : string.Empty);
            }
        }

        return null;
    }

    private static HttpResponseMessage Failure(FrankfurterFakeFailure failure) => failure switch
    {
        FrankfurterFakeFailure.ServerError => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable),
        FrankfurterFakeFailure.Unprocessable => Respond(HttpStatusCode.UnprocessableEntity, """{"status":422,"message":"invalid currency"}"""),
        _ => Respond(HttpStatusCode.OK, "[]"),
    };

    private static HttpResponseMessage Respond(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}
