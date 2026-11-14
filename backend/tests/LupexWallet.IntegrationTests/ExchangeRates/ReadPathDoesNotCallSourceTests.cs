using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.IntegrationTests.Reporting;
using Xunit;

namespace LupexWallet.IntegrationTests.ExchangeRates;

/// <summary>
/// ADR-0004 §8: no GET touches the rate service. The counter of FrankfurterFake is reset after the preparation and must stay at zero
/// for every read, with and without loaded history, for the current total and a past one.
/// </summary>
public sealed class ReadPathDoesNotCallSourceTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task PrepareAsync(bool loadHistory)
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        var usd = await Client.CreateCurrencyAsync("USD", "Dollar");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной", initialBalanceAmount: 10m, accountingStartDate: Today.AddDays(-60));
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD", initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-60));
        if (loadHistory)
        {
            await Fixture.RunExchangeRatesBackgroundPassAsync();
        }
        else
        {
            (await Client.PostAsync("/api/v1/exchange-rates/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _fake.ResetCounters();
    }

    private async Task AssertReadsDoNotCallTheServiceAsync()
    {
        string[] urls =
        [
            "/api/v1/reporting/total-amount",
            $"/api/v1/reporting/total-amount?date={Today.AddDays(-20):yyyy-MM-dd}",
            $"/api/v1/reporting/total-amount?date={Today.AddDays(-59):yyyy-MM-dd}",
            "/api/v1/exchange-rates/latest",
            "/api/v1/currencies",
            "/api/v1/currencies/available",
        ];

        foreach (var url in urls)
        {
            var response = await Client.GetAsync(url);

            response.StatusCode.Should().Be(HttpStatusCode.OK, url);
            _fake.TotalCalls.Should().Be(0, $"GET {url} must read the database only (ADR-0004 §8)");
        }
    }

    [Fact]
    public async Task Reads_AfterTheHistoryWasLoaded_DoNotCallTheService()
    {
        await PrepareAsync(loadHistory: true);

        await AssertReadsDoNotCallTheServiceAsync();
    }

    [Fact]
    public async Task Reads_WithoutAnyLoadedHistory_DoNotCallTheServiceAndExcludeTheWalletWithAReason()
    {
        await PrepareAsync(loadHistory: false);

        await AssertReadsDoNotCallTheServiceAsync();

        var past = await Client.GetFromJsonAsync<TotalAmountResponse>($"/api/v1/reporting/total-amount?date={Today.AddDays(-20):yyyy-MM-dd}");
        past!.ExcludedWallets.Should().ContainSingle(w => !string.IsNullOrWhiteSpace(w.Reason), "no history is loaded yet: the USD wallet is excluded, not fetched");
        _fake.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task Reads_OfAnEmptyApplication_DoNotCallTheService()
    {
        await AuthenticateAsync();

        foreach (var url in new[] { "/api/v1/exchange-rates/latest", "/api/v1/currencies", "/api/v1/currencies/available" })
        {
            (await Client.GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        _fake.TotalCalls.Should().Be(0);
    }
}
