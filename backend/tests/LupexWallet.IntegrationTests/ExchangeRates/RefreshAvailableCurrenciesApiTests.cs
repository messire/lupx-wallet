using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.ExchangeRates;

/// <summary>
/// POST /exchange-rates/refresh and the available-currency list (ADR-0004 §7): every refresh reloads the
/// list (also without a primary wallet), a failed or empty load keeps the stored list and is a 502, rates are requested
/// only for active currencies, active currencies take the service English names with an audit "Updated" record.
/// </summary>
public sealed class RefreshAvailableCurrenciesApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private async Task<IReadOnlyList<AvailableCurrencyResponse>> GetAvailableAsync() =>
        (await Client.GetFromJsonAsync<List<AvailableCurrencyResponse>>("/api/v1/currencies/available"))!;

    private Task<HttpResponseMessage> RefreshAsync() => Client.PostAsync("/api/v1/exchange-rates/refresh", null);

    [Fact]
    public async Task Refresh_WithoutPrimaryWallet_LoadsTheAvailableListAndReturnsOk()
    {
        await AuthenticateAsync();
        (await GetAvailableAsync()).Should().BeEmpty();

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var available = await GetAvailableAsync();
        available.Select(c => c.Code).Should().Equal("BTC", "CHF", "EUR", "GBP", "JPY", "USD");
        available.Should().ContainSingle(c => c.Code == "EUR").Which.Name.Should().Be("Euro");
        available.Should().OnlyContain(c => !c.IsActive);
        _fake.CurrenciesCalls.Should().Be(1);
        _fake.RatesCalls.Should().Be(0, "no primary wallet: no pairs to refresh");
    }

    [Fact]
    public async Task Refresh_CurrencyListFails_Returns502AndTheStoredListIsUnchanged()
    {
        await AuthenticateAsync();
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await GetAvailableAsync();
        _fake.SetCatalog(("AAA", "Changed"));
        _fake.CurrenciesFailure = FrankfurterFakeFailure.ServerError;

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await GetAvailableAsync()).Should().Equal(before);
    }

    [Fact]
    public async Task Refresh_EmptyCurrencyList_Returns502AndTheStoredListIsUnchanged()
    {
        await AuthenticateAsync();
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await GetAvailableAsync();
        _fake.SetCatalog();

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await GetAvailableAsync()).Should().Equal(before, "an empty answer is a failed load, not 'nothing is available'");
    }

    [Fact]
    public async Task Refresh_CurrencyListRejectedWith422_Returns502AndTheStoredListIsUnchanged()
    {
        await AuthenticateAsync();
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        var before = await GetAvailableAsync();
        _fake.CurrenciesFailure = FrankfurterFakeFailure.Unprocessable;

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        (await GetAvailableAsync()).Should().Equal(before);
    }

    [Fact]
    public async Task Refresh_ServiceDroppedACode_RemovesItFromTheAvailableListButAnActiveCurrencyStays()
    {
        await AuthenticateAsync();
        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsJsonAsync("/api/v1/currencies", new { Code = "JPY" })).StatusCode.Should().Be(HttpStatusCode.Created);
        _fake.SetCatalog(("EUR", "Euro"), ("USD", "United States Dollar"));

        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        (await GetAvailableAsync()).Select(c => c.Code).Should().Equal("EUR", "USD");
        var active = await Client.GetFromJsonAsync<CurrencyPageResponse>("/api/v1/currencies?limit=100");
        active!.Data.Should().ContainSingle(c => c.Code == "JPY", "the service dropped it, but it stays active (ADR-0004 §6)");
    }

    [Fact]
    public async Task Refresh_CatalogHasMoreThanTheActiveCurrencies_RatesAreRequestedOnlyForActiveOnes()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        var usd = await Client.CreateCurrencyAsync("USD", "Dollar");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной");
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD");

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _fake.RatesRequests.Select(r => (r.Base, r.Quote)).Should().Equal(
            [("USD", "EUR")], "GBP, JPY, CHF and BTC are offered by the service but not active");
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.Rates.Should().ContainSingle(r => r.FromCurrencyId == usd.Id && r.ToCurrencyId == eur.Id);
    }

    [Fact]
    public async Task Refresh_CurrencyListFailsButPairsSucceed_StillUpdatesRatesAndReturns502()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        var usd = await Client.CreateCurrencyAsync("USD", "Dollar");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной");
        await Client.CreateWalletAsync(currencyId: usd.Id, name: "USD");
        _fake.CurrenciesFailure = FrankfurterFakeFailure.ServerError;

        var response = await RefreshAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadFromJsonAsync<LatestExchangeRatesResponse>();
        body!.Rates.Should().ContainSingle(r => r.FromCurrencyId == usd.Id, "a failed list does not stop the pairs");
    }

    [Fact]
    public async Task Refresh_ActiveCurrencyWithAnOldName_TakesTheServiceNameAndAuditsUpdatedWithTheUserActor()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Евро");

        (await RefreshAsync()).StatusCode.Should().Be(HttpStatusCode.OK);

        var active = await Client.GetFromJsonAsync<CurrencyPageResponse>("/api/v1/currencies?limit=100");
        active!.Data.Should().ContainSingle().Which.Name.Should().Be("Euro");
        var updated = (await Client.GetAuditEntriesAsync("Currency", eur.Id)).Where(e => e.Action == "Updated").ToList();
        var entry = updated.Should().ContainSingle().Subject;
        entry.ActorKind.Should().Be("User", "the manual refresh runs in the caller's request");
        entry.Changes.Should().ContainSingle(c => c.Field == "Name" && c.OldValue == "Евро" && c.NewValue == "Euro");
    }

    [Fact]
    public async Task BackgroundPass_ActiveCurrencyWithAnOldName_AuditsUpdatedWithTheSystemActor()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Евро");

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var updated = (await Client.GetAuditEntriesAsync("Currency", eur.Id)).Where(e => e.Action == "Updated").ToList();
        var entry = updated.Should().ContainSingle().Subject;
        entry.ActorKind.Should().Be("System");
        entry.Changes.Should().ContainSingle(c => c.Field == "Name" && c.NewValue == "Euro");
    }

    private sealed record CurrencyPageResponse(List<CurrencyResponse> Data);
}
