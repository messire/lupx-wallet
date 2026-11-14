using System.Globalization;
using System.Net;
using System.Text;
using FluentAssertions;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// FrankfurterClient against the Frankfurter API v2 contract (ADR-0004, §1, §6): request
/// format, array response parsing, permanent "unsupported" signals (empty array, 422), source failures (404, malformed body)
/// and retries on transient failures. No network — the HTTP handler is a stub.
/// </summary>
public sealed class FrankfurterClientTests
{
    private readonly List<Uri> _requests = [];

    private FrankfurterClient CreateClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(request =>
        {
            _requests.Add(request.RequestUri!);
            return respond(request);
        });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.frankfurter.dev/v2/") };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(FrankfurterClient.HttpClientName).Returns(httpClient);
        return new FrankfurterClient(factory, NullLogger<FrankfurterClient>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string json) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task GetLatestRate_ParsesArrayAndUsesV2Query()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """[{"date":"2026-09-30","base":"GBP","quote":"EUR","rate":1.1677}]"""));

        var rate = await client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        rate.Should().Be(1.1677m);
        _requests.Should().ContainSingle().Which.ToString()
            .Should().Be("https://api.frankfurter.dev/v2/rates?base=GBP&quotes=EUR");
    }

    [Fact]
    public async Task GetLatestRate_PicksRequestedQuoteFromMultiElementArray()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"date":"2026-09-30","base":"USD","quote":"EUR","rate":0.88081},{"date":"2026-09-30","base":"USD","quote":"GBP","rate":0.75432}]"""));

        var rate = await client.GetLatestRateAsync("USD", "gbp", CancellationToken.None);

        rate.Should().Be(0.75432m);
    }

    [Fact]
    public async Task GetRatesForPeriod_ParsesSeriesOrderedByDateAndUsesFromToQuery()
    {
        // The service skips non-trading days and may return the series in any order.
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"date":"1999-01-05","base":"USD","quote":"EUR","rate":0.8547},{"date":"1999-01-04","base":"USD","quote":"EUR","rate":0.85657}]"""));

        var rates = await client.GetRatesForPeriodAsync("USD", "EUR", new DateOnly(1999, 1, 1), new DateOnly(1999, 1, 10), CancellationToken.None);

        rates.Should().Equal([(new DateOnly(1999, 1, 4), 0.85657m), (new DateOnly(1999, 1, 5), 0.8547m)]);
        _requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be("/v2/rates?base=USD&quotes=EUR&from=1999-01-01&to=1999-01-10");
    }

    [Fact]
    public async Task GetRatesForPeriod_EmptyArray_ReturnsEmptyListWithoutRetry()
    {
        // No data in the range (the currency's data starts later) is an answer, not a failure.
        var client = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var rates = await client.GetRatesForPeriodAsync("GBP", "EUR", new DateOnly(1990, 1, 1), new DateOnly(1990, 12, 31), CancellationToken.None);

        rates.Should().BeEmpty();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRatesForPeriod_UnprocessableEntity_ThrowsUnsupportedWithoutRetry()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.UnprocessableEntity, """{"status":422,"message":"invalid currency: XXX"}"""));

        var act = () => client.GetRatesForPeriodAsync("XXX", "EUR", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnsupportedCurrencyException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetRatesForPeriod_ServerError_RetriesThreeTimesThenThrowsUnavailable()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway));

        var act = () => client.GetRatesForPeriodAsync("USD", "EUR", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("""[{"base":"USD","quote":"EUR","rate":0.9}]""")]
    [InlineData("""[{"date":"not-a-date","base":"USD","quote":"EUR","rate":0.9}]""")]
    [InlineData("""[{"date":"2024-01-02","base":"USD","quote":"EUR","rate":"0.9"}]""")]
    [InlineData("""[{"date":"2024-01-02","base":"USD","quote":"EUR","rate":0}]""")]
    [InlineData("""{"rates":{}}""")]
    public async Task GetRatesForPeriod_MalformedBody_ThrowsUnavailableWithoutRetry(string json)
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var act = () => client.GetRatesForPeriodAsync("USD", "EUR", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 31), CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetCurrencies_ParsesIsoCodeAndName()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"iso_code":"AED","iso_numeric":"784","name":"United Arab Emirates Dirham","symbol":"x","start_date":"1996-04-11","end_date":"2026-10-01"},{"iso_code":"EUR","name":"Euro"}]"""));

        var currencies = await client.GetCurrenciesAsync(CancellationToken.None);

        currencies.Should().Equal([new FrankfurterCurrency("AED", "United Arab Emirates Dirham"), new FrankfurterCurrency("EUR", "Euro")]);
        _requests.Should().ContainSingle().Which.ToString().Should().Be("https://api.frankfurter.dev/v2/currencies");
    }

    [Fact]
    public async Task GetCurrencies_MissingOrBlankName_FallsBackToTheCode()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"iso_code":"XAU"},{"iso_code":"XAG","name":"  "},{"iso_code":"BTC","name":null},{"iso_code":" CHF ","name":" Swiss Franc "}]"""));

        var currencies = await client.GetCurrenciesAsync(CancellationToken.None);

        currencies.Should().Equal(
            new FrankfurterCurrency("XAU", "XAU"),
            new FrankfurterCurrency("XAG", "XAG"),
            new FrankfurterCurrency("BTC", "BTC"),
            new FrankfurterCurrency("CHF", "Swiss Franc"));
    }

    [Fact]
    public async Task GetCurrencies_DuplicateCodes_KeepsTheFirstOccurrenceCaseInsensitively()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK,
            """[{"iso_code":"EUR","name":"Euro"},{"iso_code":"eur","name":"Duplicate"},{"iso_code":"USD","name":"Dollar"}]"""));

        var currencies = await client.GetCurrenciesAsync(CancellationToken.None);

        currencies.Should().Equal(new FrankfurterCurrency("EUR", "Euro"), new FrankfurterCurrency("USD", "Dollar"));
    }

    [Fact]
    public async Task GetCurrencies_EmptyArray_ReturnsEmptyListLeavingTheDecisionToTheCaller()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var currencies = await client.GetCurrenciesAsync(CancellationToken.None);

        currencies.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCurrencies_ServerError_RetriesThreeTimesThenThrowsUnavailable()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => client.GetCurrenciesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("""{"currencies":[]}""")]
    [InlineData("""[{"name":"Euro"}]""")]
    [InlineData("""[{"iso_code":"  ","name":"Euro"}]""")]
    [InlineData("""["EUR"]""")]
    public async Task GetCurrencies_UnexpectedShapeOrEmptyCode_ThrowsUnavailable(string json)
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var act = () => client.GetCurrenciesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
    }

    [Fact]
    public async Task GetCurrencies_UnprocessableEntity_ThrowsUnavailable()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.UnprocessableEntity, "{}"));

        var act = () => client.GetCurrenciesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
    }

    [Fact]
    public async Task GetLatestRate_EmptyArray_ThrowsUnsupportedWithoutRetry()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var act = () => client.GetLatestRateAsync("BTC", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnsupportedCurrencyException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLatestRate_UnprocessableEntity_ThrowsUnsupportedWithoutRetry()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.UnprocessableEntity, """{"status":422,"message":"invalid currency: XXX"}"""));

        var act = () => client.GetLatestRateAsync("XXX", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnsupportedCurrencyException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLatestRate_NotFound_ThrowsUnavailableWithoutRetry()
    {
        // 404 means a wrong base address, not an unsupported pair.
        var client = CreateClient(_ => Json(HttpStatusCode.NotFound, """{"message":"not found"}"""));

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLatestRate_NonJsonBody_ThrowsUnavailableWithoutRetry()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html><body>Bad gateway</body></html>", Encoding.UTF8, "text/html"),
        });

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        (await act.Should().ThrowAsync<FrankfurterUnavailableException>())
            .And.InnerException.Should().BeAssignableTo<System.Text.Json.JsonException>();
        _requests.Should().HaveCount(1);
    }

    [Theory]
    [InlineData("""[{"date":"2026-09-30","base":"GBP","quote":"EUR","rate":"1.1"}]""")]
    [InlineData("""[{"date":"2026-09-30","base":"GBP","quote":"EUR","rate":null}]""")]
    [InlineData("""[{"date":"2026-09-30","base":"GBP","quote":"EUR"}]""")]
    [InlineData("""[{"date":"2026-09-30","base":"GBP","quote":"EUR","rate":1e400}]""")]
    public async Task GetLatestRate_RateNotADecimal_ThrowsUnavailableWithoutRetry(string json)
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, json));

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLatestRate_QuoteNotAString_SkipsElementAndReportsNoData()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """[{"quote":42,"rate":1.1}]"""));

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnsupportedCurrencyException>();
    }

    [Fact]
    public async Task GetLatestRate_CancelledByCaller_ThrowsOperationCanceledWithoutRetry()
    {
        using var cts = new CancellationTokenSource();
        var client = CreateClient(_ =>
        {
            cts.Cancel();
            throw new TaskCanceledException();
        });

        var act = () => client.GetLatestRateAsync("GBP", "EUR", cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        _requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetLatestRate_TimeoutWithoutCallerCancellation_RetriesThenThrowsUnavailable()
    {
        var client = CreateClient(_ => throw new TaskCanceledException());

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData("https://api.frankfurter.dev/v2", "https://api.frankfurter.dev/v2/")]
    [InlineData("https://api.frankfurter.dev/v2/", "https://api.frankfurter.dev/v2/")]
    [InlineData("  https://api.frankfurter.dev/v2  ", "https://api.frankfurter.dev/v2/")]
    public void NormalizeBaseAddress_EnsuresTrailingSlash(string input, string expected)
    {
        var baseAddress = ExchangeRatesModuleExtensions.NormalizeBaseAddress(input);

        baseAddress.ToString().Should().Be(expected);
        new Uri(baseAddress, "rates?base=GBP&quotes=EUR").ToString()
            .Should().Be("https://api.frankfurter.dev/v2/rates?base=GBP&quotes=EUR");
    }

    [Fact]
    public async Task GetLatestRate_ServerError_RetriesThreeTimesThenThrowsUnavailable()
    {
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
        _requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetLatestRate_TransientFailureThenSuccess_ReturnsRate()
    {
        var attempt = 0;
        var client = CreateClient(_ => ++attempt < 2
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Json(HttpStatusCode.OK, """[{"date":"2026-09-30","base":"GBP","quote":"EUR","rate":1.25}]"""));

        var rate = await client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        rate.Should().Be(1.25m);
        _requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetLatestRate_ObjectInsteadOfArray_ThrowsUnavailable()
    {
        // The old v1 shape must not be silently accepted as a valid response.
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """{"amount":1,"base":"GBP","date":"2024-01-01","rates":{"EUR":1.1}}"""));

        var act = () => client.GetLatestRateAsync("GBP", "EUR", CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnavailableException>();
    }

    [Fact]
    public async Task GetLatestRate_VerySmallRate_IsParsedExactly()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, "[{\"date\":\"2026-09-30\",\"base\":\"A\",\"quote\":\"B\",\"rate\":" + 0.00001m.ToString(CultureInfo.InvariantCulture) + "}]"));

        var rate = await client.GetLatestRateAsync("A", "B", CancellationToken.None);

        rate.Should().Be(0.00001m);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
