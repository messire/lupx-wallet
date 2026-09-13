using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Wallets;

/// <summary>
/// Wallets API (docs/api/openapi.yaml, тег Wallets) — UC-01…UC-07. Отказные ветки и
/// маршрутизация HTTP-кодов; сами доменные инварианты (Archive/MarkAsPrimary/ChangeCurrency/
/// EnsureCanBeDeleted) уже покрыты на уровне домена в LupexWallet.UnitTests.
/// </summary>
public sealed class WalletsApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task CreateWallet_FirstWallet_IsPrimaryAndIncludedInTotal()
    {
        await AuthenticateAsync();

        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 500m, name: "Основной");

        wallet.IsPrimary.Should().BeTrue("первый созданный кошелек автоматически становится основным (Q7)");
        wallet.IncludeInTotal.Should().BeTrue("основной кошелек всегда включен в общую сумму (Q8)");
        wallet.CurrentBalance.Amount.Should().Be("500");
    }

    [Fact]
    public async Task CreateWallet_SecondWallet_IsNotPrimary()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Первый");

        var second = await Client.CreateWalletAsync(name: "Второй");

        second.IsPrimary.Should().BeFalse();
    }

    // ---- W2.5, случай d: валидация walletTypeId/currencyId ----

    [Fact]
    public async Task CreateWallet_UnknownWalletTypeId_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = "Кошелек",
            WalletTypeId = Guid.NewGuid(),
            CurrencyId = currency.Id,
            InitialBalanceAmount = 0m,
            AccountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWallet_InactiveWalletTypeId_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync("Скоро деактивирован");
        var currency = await Client.CreateCurrencyAsync();
        await Client.PostAsync($"/api/v1/wallet-types/{walletType.Id}/deactivate", content: null);

        var response = await Client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = "Кошелек",
            WalletTypeId = walletType.Id,
            CurrencyId = currency.Id,
            InitialBalanceAmount = 0m,
            AccountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWallet_UnknownCurrencyId_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = "Кошелек",
            WalletTypeId = walletType.Id,
            CurrencyId = Guid.NewGuid(),
            InitialBalanceAmount = 0m,
            AccountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpdateWallet_UnknownWalletTypeId_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Кошелек");

        var response = await Client.PatchAsJsonAsync($"/api/v1/wallets/{wallet.Id}", new
        {
            Name = "Кошелек",
            WalletTypeId = Guid.NewGuid(),
            PurposeDescription = (string?)null,
            IncludeInTotal = true,
            DisplayOrder = 0,
            Color = (string?)null,
            Icon = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateWallet_WithBlankName_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync();
        var currency = await Client.CreateCurrencyAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/wallets", new
        {
            Name = "   ",
            WalletTypeId = walletType.Id,
            CurrencyId = currency.Id,
            InitialBalanceAmount = 0m,
            AccountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetWallet_ExistingWallet_ReturnsIt()
    {
        await AuthenticateAsync();
        var created = await Client.CreateWalletAsync(name: "Карта");

        var response = await Client.GetAsync($"/api/v1/wallets/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        body!.Id.Should().Be(created.Id);
    }

    [Fact]
    public async Task GetWallet_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.GetAsync($"/api/v1/wallets/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListWallets_ReturnsCreatedWallets()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "А");
        await Client.CreateWalletAsync(name: "Б");

        var response = await Client.GetAsync("/api/v1/wallets");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WalletPageResponse>();
        body!.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task UpdateWallet_ValidRequest_ReturnsUpdatedWallet()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Старое имя");

        var response = await Client.PatchAsJsonAsync($"/api/v1/wallets/{wallet.Id}", new
        {
            Name = "Новое имя",
            wallet.WalletTypeId,
            PurposeDescription = (string?)null,
            IncludeInTotal = wallet.IncludeInTotal,
            DisplayOrder = 7,
            Color = (string?)null,
            Icon = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        body!.Name.Should().Be("Новое имя");
        body.DisplayOrder.Should().Be(7);
    }

    [Fact]
    public async Task UpdateWallet_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync();

        var response = await Client.PatchAsJsonAsync($"/api/v1/wallets/{Guid.NewGuid()}", new
        {
            Name = "Имя",
            WalletTypeId = walletType.Id,
            PurposeDescription = (string?)null,
            IncludeInTotal = true,
            DisplayOrder = 0,
            Color = (string?)null,
            Icon = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ArchiveWallet_NonPrimaryWallet_ReturnsArchivedWallet()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной"); // становится основным первым
        var secondary = await Client.CreateWalletAsync(name: "Второй");

        var response = await Client.PostAsync($"/api/v1/wallets/{secondary.Id}/archive", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        body!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task ArchiveWallet_PrimaryWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var primary = await Client.CreateWalletAsync(name: "Основной");

        var response = await Client.PostAsync($"/api/v1/wallets/{primary.Id}/archive", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "нельзя архивировать текущий основной кошелек (Q7)");
    }

    [Fact]
    public async Task ArchiveWallet_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.PostAsync($"/api/v1/wallets/{Guid.NewGuid()}/archive", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SetPrimaryWallet_ArchivedWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var secondary = await Client.CreateWalletAsync(name: "Второй");
        await Client.PostAsync($"/api/v1/wallets/{secondary.Id}/archive", content: null);

        var response = await Client.PostAsync($"/api/v1/wallets/{secondary.Id}/set-primary", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "нельзя назначить архивный кошелек основным");
    }

    [Fact]
    public async Task SetPrimaryWallet_ThreeWallets_ExactlyOneRemainsPrimaryAfterEachTransfer()
    {
        await AuthenticateAsync();
        var first = await Client.CreateWalletAsync(name: "Первый");
        var second = await Client.CreateWalletAsync(name: "Второй");
        var third = await Client.CreateWalletAsync(name: "Третий");
        first.IsPrimary.Should().BeTrue("первый созданный кошелек становится основным (Q7)");

        var toSecond = await Client.PostAsync($"/api/v1/wallets/{second.Id}/set-primary", content: null);
        toSecond.StatusCode.Should().Be(HttpStatusCode.OK);

        var toThird = await Client.PostAsync($"/api/v1/wallets/{third.Id}/set-primary", content: null);
        toThird.StatusCode.Should().Be(HttpStatusCode.OK);

        var refreshedFirst = await (await Client.GetAsync($"/api/v1/wallets/{first.Id}")).Content.ReadFromJsonAsync<WalletResponse>();
        var refreshedSecond = await (await Client.GetAsync($"/api/v1/wallets/{second.Id}")).Content.ReadFromJsonAsync<WalletResponse>();
        var refreshedThird = await (await Client.GetAsync($"/api/v1/wallets/{third.Id}")).Content.ReadFromJsonAsync<WalletResponse>();

        var primaryFlags = new[] { refreshedFirst!.IsPrimary, refreshedSecond!.IsPrimary, refreshedThird!.IsPrimary };
        primaryFlags.Count(isPrimary => isPrimary).Should().Be(1, "инвариант: ровно один основной кошелек");
        refreshedThird.IsPrimary.Should().BeTrue();
        refreshedThird.IncludeInTotal.Should().BeTrue("Q8: основной кошелек всегда включен в общую сумму");
    }

    [Fact]
    public async Task DeleteWallet_PrimaryWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var primary = await Client.CreateWalletAsync(name: "Основной");

        var response = await Client.DeleteAsync($"/api/v1/wallets/{primary.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "нельзя удалить основной кошелек (Q7)");
    }

    [Fact]
    public async Task DeleteWallet_NonPrimaryWithoutHistory_ReturnsNoContentAndWalletDisappears()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var secondary = await Client.CreateWalletAsync(name: "Второй");

        var response = await Client.DeleteAsync($"/api/v1/wallets/{secondary.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var getResponse = await Client.GetAsync($"/api/v1/wallets/{secondary.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteWallet_WithOperationHistory_ReturnsConflict()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var secondary = await Client.CreateWalletAsync(name: "Второй", initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationResponse = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = secondary.Id,
            OperationTypeId = incomeTypeId,
            Amount = 10m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        operationResponse.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/v1/wallets/{secondary.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "у кошелька уже есть история операций (ADR-0009, случай a)");
    }

    [Fact]
    public async Task ChangeWalletCurrency_WithoutHistory_ReturnsUpdatedWallet()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Кошелек");
        var newCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<WalletResponse>();
        body!.CurrencyId.Should().Be(newCurrency.Id);
    }

    [Fact]
    public async Task ChangeWalletCurrency_WithHistory_ReturnsConflict()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(name: "Кошелек", initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationResponse = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 10m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        operationResponse.EnsureSuccessStatusCode();
        var newCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "у кошелька уже есть операции (Q15, ADR-0009, случай a)");
    }

    private sealed record CursorPageMeta(string? NextCursor, bool HasMore);
    private sealed record WalletPageResponse(IReadOnlyList<WalletResponse> Data, CursorPageMeta Pagination);
}
