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
    public async Task GetHistoricalRate_ReturnsActualDateFromResponse()
    {
        // A non-trading date: the service answers with the closest earlier date.
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """[{"date":"1999-01-01","base":"USD","quote":"EUR","rate":0.85657}]"""));

        var (rate, asOf) = await client.GetHistoricalRateAsync("USD", "EUR", new DateOnly(1999, 1, 2), CancellationToken.None);

        rate.Should().Be(0.85657m);
        asOf.Should().Be(new DateOnly(1999, 1, 1));
        _requests.Should().ContainSingle().Which.PathAndQuery
            .Should().Be("/v2/rates?base=USD&quotes=EUR&date=1999-01-02");
    }

    [Fact]
    public async Task GetHistoricalRate_ResponseWithoutDate_FallsBackToRequestedDate()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """[{"base":"USD","quote":"EUR","rate":0.9}]"""));

        var (_, asOf) = await client.GetHistoricalRateAsync("USD", "EUR", new DateOnly(2024, 5, 6), CancellationToken.None);

        asOf.Should().Be(new DateOnly(2024, 5, 6));
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
    public async Task GetHistoricalRate_EmptyArray_ThrowsUnsupported()
    {
        // A date before the start of the service's data also yields 200 [].
        var client = CreateClient(_ => Json(HttpStatusCode.OK, "[]"));

        var act = () => client.GetHistoricalRateAsync("GBP", "EUR", new DateOnly(1990, 1, 1), CancellationToken.None);

        await act.Should().ThrowAsync<FrankfurterUnsupportedCurrencyException>();
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
    public async Task GetHistoricalRate_DateNotAString_FallsBackToRequestedDate()
    {
        var client = CreateClient(_ => Json(HttpStatusCode.OK, """[{"date":20240506,"base":"USD","quote":"EUR","rate":0.9}]"""));

        var (_, asOf) = await client.GetHistoricalRateAsync("USD", "EUR", new DateOnly(2024, 5, 6), CancellationToken.None);

        asOf.Should().Be(new DateOnly(2024, 5, 6));
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
