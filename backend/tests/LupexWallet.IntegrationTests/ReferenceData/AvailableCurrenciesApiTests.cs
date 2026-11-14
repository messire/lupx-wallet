using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.ReferenceData.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.ReferenceData;

/// <summary>
/// Available and active currencies (ADR-0004 §7, docs/api/openapi.yaml): GET /currencies/available,
/// POST /currencies {code} from the available list, DELETE as "remove from the active set", and the
/// listing of active currencies. The rate service is replaced by FrankfurterFake and must never be called by these reads.
/// </summary>
public sealed class AvailableCurrenciesApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private async Task<IReadOnlyList<AvailableCurrencyResponse>> GetAvailableAsync()
    {
        var response = await Client.GetAsync("/api/v1/currencies/available");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<List<AvailableCurrencyResponse>>())!;
    }

    private async Task<IReadOnlyList<string>> GetActiveCodesAsync()
    {
        var response = await Client.GetAsync("/api/v1/currencies?limit=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = (await response.Content.ReadFromJsonAsync<CurrencyPage>())!;
        return page.Data.Select(c => c.Code).Order().ToList();
    }

    [Fact]
    public async Task GetAvailable_BeforeAnyLoad_ReturnsEmptyArrayWithoutCallingTheService()
    {
        await AuthenticateAsync();

        var available = await GetAvailableAsync();

        available.Should().BeEmpty();
        _fake.TotalCalls.Should().Be(0);
    }

    [Fact]
    public async Task GetAvailable_ReturnsAllSortedByCodeWithIsActiveFlag()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("ZZA", "Zeta");
        await TestDataBuilder.SeedAvailableCurrencyAsync("AAA", "Alpha");
        await TestDataBuilder.SeedAvailableCurrencyAsync("MMM", "Mu");
        await TestDataBuilder.SeedAvailableCurrencyAsync("CCC", "Chi");
        await Client.CreateCurrencyAsync("MMM", "Mu");

        var available = await GetAvailableAsync();

        available.Select(c => c.Code).Should().Equal("AAA", "CCC", "MMM", "ZZA");
        available.Should().Equal(
            [
                new AvailableCurrencyResponse("AAA", "Alpha", false),
                new AvailableCurrencyResponse("CCC", "Chi", false),
                new AvailableCurrencyResponse("MMM", "Mu", true),
                new AvailableCurrencyResponse("ZZA", "Zeta", false),
            ]);
        _fake.TotalCalls.Should().Be(0, "the list is read from the database (ADR-0004 §7, §8)");
    }

    [Fact]
    public async Task PostCurrency_AvailableCode_Returns201WithTheNameFromTheAvailableList()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("EUR", "Euro");

        var response = await Client.PostAsJsonAsync("/api/v1/currencies", new { Code = " eur " });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var currency = (await response.Content.ReadFromJsonAsync<CurrencyResponse>())!;
        currency.Code.Should().Be("EUR");
        currency.Name.Should().Be("Euro", "the name comes from the available list, the request carries only the code");
        response.Headers.Location!.ToString().Should().EndWith($"/api/v1/currencies/{currency.Id}");
        (await GetAvailableAsync()).Should().ContainSingle(c => c.Code == "EUR" && c.IsActive);
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PostCurrency_UnknownOrBlankCode_Returns400AndCreatesNothing(string code)
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("EUR", "Euro");

        var response = await Client.PostAsJsonAsync("/api/v1/currencies", new { Code = code });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetActiveCodesAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task PostCurrency_WithoutCodeProperty_Returns400()
    {
        await AuthenticateAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/currencies", new { });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostCurrency_AlreadyActiveCode_Returns409()
    {
        await AuthenticateAsync();
        await Client.CreateCurrencyAsync("EUR", "Euro");

        var response = await Client.PostAsJsonAsync("/api/v1/currencies", new { Code = "EUR" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Валюта EUR уже добавлена в активные.");
        (await GetActiveCodesAsync()).Should().Equal("EUR");
    }

    [Fact]
    public async Task PostCurrency_SixParallelRequestsForOneCode_OneCreatedTheRestConflict()
    {
        // The activation takes an advisory lock per code, so the later requests see the winner's currency and get a 409, not a 500.
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("CHF", "Swiss Franc");

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(_ => Task.Run(() => Client.PostAsJsonAsync("/api/v1/currencies", new { Code = "CHF" }))));

        var statuses = string.Join(",", responses.Select(r => (int)r.StatusCode));
        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "statuses were " + statuses);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(5, "statuses were " + statuses);
        (await GetActiveCodesAsync()).Should().Equal("CHF");
    }

    [Fact]
    public async Task DeleteCurrency_UsedByWallet_Returns409WithTextWithoutDeactivation()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR", "Euro");
        await Client.CreateWalletAsync(currencyId: currency.Id);

        var response = await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Валюта EUR используется");
        body.Should().NotContain(currency.Id.ToString(), "the message is for the user, identifiers stay out of it");
        body.ToLowerInvariant().Should().NotContain("деактивац", "a currency has no deactivated state (ADR-0004 §7)");
        (await GetActiveCodesAsync()).Should().Contain("EUR");
    }

    [Fact]
    public async Task DeleteCurrency_Unused_Returns204AndItStaysAvailableButInactive()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR", "Euro");

        var response = await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetActiveCodesAsync()).Should().BeEmpty();
        (await GetAvailableAsync()).Should().ContainSingle(c => c.Code == "EUR" && !c.IsActive, "removing from the active set does not touch the available list");
    }

    [Fact]
    public async Task ListCurrencies_ReturnsOnlyActiveOnesAndIgnoresTheRemovedIncludeInactiveParameter()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("AAA", "Alpha");
        await Client.CreateCurrencyAsync("EUR", "Euro");

        var response = await Client.GetAsync("/api/v1/currencies?includeInactive=true&limit=100");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"EUR\"").And.NotContain("\"AAA\"");
        body.ToLowerInvariant().Should().NotContain("isactive", "Currency has no isActive in the contract");
    }

    [Fact]
    public async Task DeactivateRoute_NoLongerExists()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR", "Euro");

        var response = await Client.PostAsync($"/api/v1/currencies/{currency.Id}/deactivate", null);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.MethodNotAllowed);
        (await GetActiveCodesAsync()).Should().Equal("EUR");
    }

    [Fact]
    public async Task ReadingCurrencies_NeverCallsTheRateService()
    {
        await AuthenticateAsync();
        await Client.CreateCurrencyAsync("EUR", "Euro");
        _fake.ResetCounters();

        (await Client.GetAsync("/api/v1/currencies")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetAsync("/api/v1/currencies/available")).StatusCode.Should().Be(HttpStatusCode.OK);

        _fake.TotalCalls.Should().Be(0);
    }

    private sealed record CurrencyPage(List<CurrencyResponse> Data);
}

/// <summary>
/// IAvailableCurrencyCatalog (ADR-0004 §7) against the real database: upsert, removal of codes
/// the service no longer offers, an empty list as a failed load, normalisation, and renaming of active
/// currencies to the service name with a CurrencyRenamed event and an audit "Updated" record.
/// </summary>
public sealed class AvailableCurrencyCatalogTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task ReplaceAsync(params (string Code, string Name)[] items)
    {
        using var scope = Fixture.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IAvailableCurrencyCatalog>()
            .ReplaceAsync(items.Select(i => new AvailableCurrencyItem(i.Code, i.Name)).ToList(), CancellationToken.None);
    }

    private async Task<IReadOnlyList<AvailableCurrencyResponse>> GetAvailableAsync() =>
        (await Client.GetFromJsonAsync<List<AvailableCurrencyResponse>>("/api/v1/currencies/available"))!;

    [Fact]
    public async Task Replace_NewAndExistingCodes_AddsNewAndUpdatesNames()
    {
        await AuthenticateAsync();
        await ReplaceAsync(("EUR", "Euro"), ("USD", "Dollar"));

        await ReplaceAsync(("EUR", "Euro (updated)"), ("GBP", "Pound"), ("USD", "Dollar"));

        (await GetAvailableAsync()).Select(c => (c.Code, c.Name)).Should().Equal(
            ("EUR", "Euro (updated)"), ("GBP", "Pound"), ("USD", "Dollar"));
    }

    [Fact]
    public async Task Replace_CodesMissingFromTheNewList_AreRemovedFromTheAvailableListButActiveCurrenciesStay()
    {
        await AuthenticateAsync();
        await ReplaceAsync(("EUR", "Euro"), ("USD", "Dollar"), ("GBP", "Pound"));
        (await Client.PostAsJsonAsync("/api/v1/currencies", new { Code = "GBP" })).StatusCode.Should().Be(HttpStatusCode.Created);

        await ReplaceAsync(("EUR", "Euro"), ("USD", "Dollar"));

        (await GetAvailableAsync()).Select(c => c.Code).Should().Equal("EUR", "USD");
        var active = await Client.GetFromJsonAsync<CurrencyListResponse>("/api/v1/currencies?limit=100");
        active!.Data.Should().ContainSingle(c => c.Code == "GBP", "a currency the service dropped stays active (ADR-0004 §6)");
    }

    [Fact]
    public async Task Replace_EmptyListOrRowsWithoutCodeOrName_LeavesTheStoredListUntouched()
    {
        await AuthenticateAsync();
        await ReplaceAsync(("EUR", "Euro"), ("USD", "Dollar"));

        await ReplaceAsync();
        await ReplaceAsync(("  ", "No code"), ("XXX", "  "));

        (await GetAvailableAsync()).Select(c => c.Code).Should().Equal("EUR", "USD");
    }

    [Fact]
    public async Task Replace_LowercaseCodesWhitespaceAndDuplicates_AreNormalized()
    {
        await AuthenticateAsync();

        await ReplaceAsync((" eur ", " Euro "), ("EUR", "Euro (last)"), ("usd", "Dollar"), ("", "skipped"));

        (await GetAvailableAsync()).Select(c => (c.Code, c.Name)).Should().Equal(("EUR", "Euro (last)"), ("USD", "Dollar"));
    }

    [Fact]
    public async Task Replace_ActiveCurrencyWithOldName_IsRenamedWithAuditUpdatedAndInactiveOnesAreNotCreated()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Евро");

        await ReplaceAsync(("EUR", "Euro"), ("USD", "Dollar"));

        var active = await Client.GetFromJsonAsync<CurrencyListResponse>("/api/v1/currencies?limit=100");
        active!.Data.Should().ContainSingle().Which.Name.Should().Be("Euro", "names are the service English names (ADR-0004 §7)");

        var updated = (await Client.GetAuditEntriesAsync("Currency", eur.Id)).Where(e => e.Action == "Updated").ToList();
        updated.Should().ContainSingle();
        updated[0].Changes.Should().ContainSingle(c => c.Field == "Name" && c.OldValue == "Евро" && c.NewValue == "Euro");
    }

    [Fact]
    public async Task Replace_ActiveCurrencyAlreadyWithTheServiceName_WritesNoAuditEntry()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");

        await ReplaceAsync(("EUR", "Euro"));
        await ReplaceAsync(("EUR", "Euro"));

        (await Client.GetAuditEntriesAsync("Currency", eur.Id)).Where(e => e.Action == "Updated").Should().BeEmpty();
    }

    private sealed record CurrencyListResponse(List<CurrencyResponse> Data);
}
