using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.ReferenceData;

/// <summary>
/// ReferenceData API (docs/api/openapi.yaml, теги WalletTypes/OperationTypes/Currencies/
/// OperationBehaviorKinds) — create/deactivate/delete (ADR-0002), системный справочник
/// OperationBehaviorKind (seed, 4 строки, ddd-model.md §2.3).
/// </summary>
public sealed class ReferenceDataApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    [Fact]
    public async Task OperationBehaviorKinds_AreSeededWithFourFixedRows()
    {
        await AuthenticateAsync();

        var kinds = await Client.ListOperationBehaviorKindsAsync();

        kinds.Should().HaveCount(4);
        kinds.Select(k => k.Code).Should().BeEquivalentTo(["Income", "Expense", "Transfer", "Adjustment"]);
    }

    [Fact]
    public async Task CreateWalletType_ThenDeactivate_SetsIsActiveFalse()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync("Депозит");

        var response = await Client.PostAsync($"/api/v1/wallet-types/{walletType.Id}/deactivate", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<ReferenceItemResponse>();
        body!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteWalletType_NotUsed_ReturnsNoContent()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync("Временный");

        var response = await Client.DeleteAsync($"/api/v1/wallet-types/{walletType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteWalletType_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.DeleteAsync($"/api/v1/wallet-types/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- W2.5, случай c (ADR-0009): isUsed через реальные IReferenceItemUsageProbe ----

    [Fact]
    public async Task DeleteWalletType_UsedByWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync("Используемый");
        await Client.CreateWalletAsync(name: "Кошелек", walletTypeId: walletType.Id);

        var response = await Client.DeleteAsync($"/api/v1/wallet-types/{walletType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "тип кошелька используется существующим кошельком (ADR-0009, случай c)");
    }

    [Fact]
    public async Task DeleteOperationType_UsedByOperation_ReturnsConflict()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeType = await Client.CreateOperationTypeAsync("Income");
        var operationResponse = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeType.Id,
            Amount = 10m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        operationResponse.EnsureSuccessStatusCode();

        var response = await Client.DeleteAsync($"/api/v1/operation-types/{incomeType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "тип операции используется существующей операцией (ADR-0009, случай c)");
    }

    [Fact]
    public async Task DeleteOperationType_NotUsed_ReturnsNoContent()
    {
        await AuthenticateAsync();
        var operationType = await Client.CreateOperationTypeAsync("Income", "Неиспользуемый");

        var response = await Client.DeleteAsync($"/api/v1/operation-types/{operationType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeleteCurrency_UsedByWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("QQQ", "Используемая");
        await Client.CreateWalletAsync(name: "Кошелек", currencyId: currency.Id);

        var response = await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "валюта используется существующим кошельком (ADR-0009, случай c)");
    }

    [Fact]
    public async Task DeleteCurrency_NotUsed_ReturnsNoContent()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("WWW", "Неиспользуемая");

        var response = await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CreateCurrency_DuplicateCode_ReturnsConflict()
    {
        await AuthenticateAsync();
        await Client.CreateCurrencyAsync("XYZ", "Первая");

        var response = await Client.PostAsJsonAsync("/api/v1/currencies", new { Code = "xyz", Name = "Вторая" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "код валюты уникален глобально, включая регистр (нормализуется в UPPER)");
    }

    [Fact]
    public async Task CreateOperationType_WithBehaviorKind_PersistsBehaviorKindId()
    {
        await AuthenticateAsync();
        var behaviorKindId = await Client.GetBehaviorKindIdAsync("Income");

        var response = await Client.PostAsJsonAsync("/api/v1/operation-types", new { Name = "Зарплата", BehaviorKindId = behaviorKindId });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task CreateWalletType_WithBlankName_ReturnsBadRequest()
    {
        await AuthenticateAsync();

        var response = await Client.PostAsJsonAsync("/api/v1/wallet-types", new { Name = "  " });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
