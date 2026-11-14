using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.Wallets;

/// <summary>
/// POST /wallets by currencyCode (ADR-0004 §7): a code that is available but not active becomes active in
/// the same transaction as the wallet, an active code is reused, an unknown code is a 400, and a rejected wallet rolls the
/// activation back. After the commit (and only then) the background service is asked for a pass (ADR-0004 §9);
/// PUT /wallets/{id}/currency accepts only an active currency.
/// </summary>
public sealed class WalletCurrencyActivationApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private Task<ReferenceItemResponse> CreateTypeAsync() => Client.CreateWalletTypeAsync($"Тип-{Guid.NewGuid():N}");

    private async Task<HttpResponseMessage> PostWalletAsync(Guid walletTypeId, string currencyCode, string name = "Кошелек") =>
        await Client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = name,
            WalletTypeId = walletTypeId,
            CurrencyCode = currencyCode,
            InitialBalanceAmount = 0m,
            AccountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-30),
        });

    private async Task<IReadOnlyList<CurrencyResponse>> GetActiveCurrenciesAsync()
    {
        var page = await Client.GetFromJsonAsync<CurrencyPage>("/api/v1/currencies?limit=100");
        return page!.Data;
    }

    private async Task<IReadOnlyList<AvailableCurrencyResponse>> GetAvailableAsync() =>
        (await Client.GetFromJsonAsync<List<AvailableCurrencyResponse>>("/api/v1/currencies/available"))!;

    private ExchangeRateRefreshSignal Signal => Fixture.Factory.Services.GetRequiredService<ExchangeRateRefreshSignal>();

    private int DrainSignal()
    {
        var count = 0;
        while (Signal.Reader.TryRead(out _))
        {
            count++;
        }

        return count;
    }

    private async Task<long> CountAuditEntriesAsync(string entityType, string action)
    {
        await using var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM audit.audit_entries WHERE entity_type = @type AND action = @action";
        command.Parameters.AddWithValue("type", entityType);
        command.Parameters.AddWithValue("action", action);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task CreateWallet_AvailableInactiveCode_Returns201AndTheCurrencyBecomesActiveWithAuditCreated()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("USD", "United States Dollar");
        var walletType = await CreateTypeAsync();

        var response = await PostWalletAsync(walletType.Id, "usd");

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var wallet = (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
        var active = await GetActiveCurrenciesAsync();
        active.Should().ContainSingle(c => c.Code == "USD" && c.Name == "United States Dollar" && c.Id == wallet.CurrencyId);
        (await GetAvailableAsync()).Should().ContainSingle(c => c.Code == "USD" && c.IsActive);
        (await Client.GetAuditEntriesAsync("Currency", wallet.CurrencyId)).Should().ContainSingle(e => e.Action == "Created");
    }

    [Fact]
    public async Task CreateWallet_AlreadyActiveCode_ReusesTheCurrencyWithoutADuplicate()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR", "Euro");
        var walletType = await CreateTypeAsync();

        var response = await PostWalletAsync(walletType.Id, "EUR");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var wallet = (await response.Content.ReadFromJsonAsync<WalletResponse>())!;
        wallet.CurrencyId.Should().Be(currency.Id);
        (await GetActiveCurrenciesAsync()).Should().ContainSingle(c => c.Code == "EUR");
        (await Client.GetAuditEntriesAsync("Currency", currency.Id)).Where(e => e.Action == "Created").Should().ContainSingle();
    }

    [Fact]
    public async Task CreateWallet_UnknownCode_Returns400AndCreatesNoCurrency()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("EUR", "Euro");
        var walletType = await CreateTypeAsync();

        var response = await PostWalletAsync(walletType.Id, "BTC");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetActiveCurrenciesAsync()).Should().BeEmpty();
        (await GetAvailableAsync()).Should().ContainSingle(c => c.Code == "EUR" && !c.IsActive);
    }

    [Fact]
    public async Task CreateWallet_SixParallelRequestsActivatingTheSameCode_AllSucceedAndLeaveOneCurrencyAndOnePrimaryWallet()
    {
        // Several requests would pass the "is it active" check at once; an advisory lock per code (ReferenceDataUnitOfWork.LockCurrencyCodeAsync)
        // serialises them until commit, so the later ones reuse the winner's currency instead of failing on the unique index (500).
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("USD", "United States Dollar");
        var walletType = await CreateTypeAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 6)
            .Select(i => Task.Run(() => PostWalletAsync(walletType.Id, "USD", name: $"Кошелек {i}"))));

        var statuses = string.Join(",", responses.Select(r => (int)r.StatusCode));
        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created, "statuses were " + statuses);
        var currencies = await GetActiveCurrenciesAsync();
        currencies.Should().ContainSingle(c => c.Code == "USD");
        var wallets = await Task.WhenAll(responses.Select(async r => (await r.Content.ReadFromJsonAsync<WalletResponse>())!));
        wallets.Select(w => w.CurrencyId).Distinct().Should().ContainSingle().Which.Should().Be(currencies.Single().Id);
        wallets.Count(w => w.IsPrimary).Should().Be(1, "only the first wallet is primary");
    }

    [Fact]
    public async Task CreateWallet_BlankCode_Returns400()
    {
        await AuthenticateAsync();
        var walletType = await CreateTypeAsync();

        var response = await PostWalletAsync(walletType.Id, "  ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWallet_RejectedByValidation_RollsBackTheActivationAndDeliversNoRefreshSignal()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("USD", "United States Dollar");
        var walletType = await CreateTypeAsync();
        DrainSignal();

        var response = await PostWalletAsync(walletType.Id, "USD", name: "   ");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a blank wallet name is rejected after the currency was already activated inside the transaction");
        (await GetActiveCurrenciesAsync()).Should().BeEmpty("the activation is rolled back with the wallet");
        (await GetAvailableAsync()).Should().ContainSingle(c => c.Code == "USD" && !c.IsActive);
        (await CountAuditEntriesAsync("Currency", "Created")).Should().Be(0, "the audit record rolled back too");
        DrainSignal().Should().Be(0, "a rolled-back transaction must not wake the background service (ADR-0001 §7)");
    }

    [Fact]
    public async Task CreateWallet_Committed_DeliversTheRefreshSignalAfterCommit()
    {
        await AuthenticateAsync();
        await TestDataBuilder.SeedAvailableCurrencyAsync("USD", "United States Dollar");
        var walletType = await CreateTypeAsync();
        DrainSignal();

        var response = await PostWalletAsync(walletType.Id, "USD");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        DrainSignal().Should().BeGreaterThan(0, "CurrencyCreated and WalletCreated ask for a pass once the transaction has committed");
    }

    [Fact]
    public async Task CreateWallet_NewCurrency_BackgroundPassRequestsLatestRateAndHistoryForIt()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR", "Euro");
        await Client.CreateWalletAsync(currencyId: eur.Id, name: "Основной");
        await TestDataBuilder.SeedAvailableCurrencyAsync("USD", "United States Dollar");
        var walletType = await CreateTypeAsync();
        (await PostWalletAsync(walletType.Id, "USD")).StatusCode.Should().Be(HttpStatusCode.Created);
        _fake.ResetCounters();

        await Fixture.RunExchangeRatesBackgroundPassAsync();

        _fake.RatesRequests.Should().Contain(r => r.Base == "USD" && r.Quote == "EUR" && r.From == null, "the latest rate of the new currency");
        _fake.RatesRequests.Should().Contain(r => r.Base == "USD" && r.Quote == "EUR" && r.From != null, "the history of the new currency");
    }

    [Fact]
    public async Task ChangeWalletCurrency_UnknownCurrencyId_Returns400()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Кошелек");

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetFromJsonAsync<WalletResponse>($"/api/v1/wallets/{wallet.Id}"))!.CurrencyId.Should().Be(wallet.CurrencyId);
    }

    [Fact]
    public async Task ChangeWalletCurrency_UnknownWalletAndUnknownCurrency_Returns404()
    {
        await AuthenticateAsync();

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{Guid.NewGuid()}/currency", new { CurrencyId = Guid.NewGuid() });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ChangeWalletCurrency_ToACurrencyRemovedFromTheActiveSet_Returns400()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Кошелек");
        var removed = await Client.CreateCurrencyAsync("GBP", "Pound");
        (await Client.DeleteAsync($"/api/v1/currencies/{removed.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = removed.Id });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "only an active currency is accepted");
    }

    private sealed record CurrencyPage(List<CurrencyResponse> Data);
}
