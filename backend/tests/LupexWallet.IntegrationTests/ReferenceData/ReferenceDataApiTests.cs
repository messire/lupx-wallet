using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.ExchangeRates.Domain;
using LupexWallet.ExchangeRates.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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

    // ---- W2.5, §4 (ADR-0002): isUsed через реальные IReferenceItemUsageProbe ----

    [Fact]
    public async Task DeleteWalletType_UsedByWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync("Используемый");
        await Client.CreateWalletAsync(name: "Кошелек", walletTypeId: walletType.Id);

        var response = await Client.DeleteAsync($"/api/v1/wallet-types/{walletType.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "тип кошелька используется существующим кошельком (ADR-0002, §4)");
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

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "тип операции используется существующей операцией (ADR-0002, §4)");
    }

    // ---- Soft-deleted operations count as usage; rates are not usage (ADR-0002, §4) ----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(Guid WalletId, Guid OperationId)> CreateAndSoftDeleteOperationAsync(
        Guid operationTypeId, Guid? currencyId = null)
    {
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, currencyId: currencyId);
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, operationTypeId, 10m, Today.AddDays(-1));
        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        return (wallet.Id, operationId);
    }

    [Fact]
    public async Task DeleteOperationType_UsedOnlyBySoftDeletedOperation_ReturnsConflictAndDeactivateReturnsOk()
    {
        await AuthenticateAsync();
        var incomeType = await Client.CreateOperationTypeAsync("Income");
        await CreateAndSoftDeleteOperationAsync(incomeType.Id);

        var deleteResponse = await Client.DeleteAsync($"/api/v1/operation-types/{incomeType.Id}");
        var deactivateResponse = await Client.PostAsync($"/api/v1/operation-types/{incomeType.Id}/deactivate", null);

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "a soft-deleted operation still references the type (ADR-0002, §4)");
        deactivateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivateResponse.Content.ReadFromJsonAsync<ReferenceItemResponse>())!.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteOperationType_AfterItsOnlyOperationIsRestoredThenSoftDeletedAgain_StillReturnsConflict()
    {
        await AuthenticateAsync();
        var incomeType = await Client.CreateOperationTypeAsync("Income");
        var (_, operationId) = await CreateAndSoftDeleteOperationAsync(incomeType.Id);
        await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null);

        (await Client.DeleteAsync($"/api/v1/operation-types/{incomeType.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        await Client.DeleteAsync($"/api/v1/operations/{operationId}");

        (await Client.DeleteAsync($"/api/v1/operation-types/{incomeType.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task OperationsUsageProbe_SoftDeletedOperation_ReportsItsCurrencyAndOperationTypeAsUsed()
    {
        // Through the API a wallet of the same currency also makes the currency "used", so the Operations-side
        // rule (no deleted_at filter) is asserted on its own via the registered port implementation.
        await AuthenticateAsync();
        var incomeType = await Client.CreateOperationTypeAsync("Income");
        var currency = await Client.CreateCurrencyAsync("PRB", "Проба");
        var unrelatedCurrency = await Client.CreateCurrencyAsync("PRC", "Без операций");
        var unrelatedType = await Client.CreateOperationTypeAsync("Expense", "Не используется");
        await CreateAndSoftDeleteOperationAsync(incomeType.Id, currency.Id);
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var probe = scope.ServiceProvider.GetServices<IReferenceItemUsageProbe>().OfType<OperationsReferenceItemUsageProbe>().Single();

        (await probe.IsUsedAsync(ReferenceItemKind.Currency, currency.Id, CancellationToken.None)).Should().BeTrue();
        (await probe.IsUsedAsync(ReferenceItemKind.OperationType, incomeType.Id, CancellationToken.None)).Should().BeTrue();
        (await probe.IsUsedAsync(ReferenceItemKind.Currency, unrelatedCurrency.Id, CancellationToken.None)).Should().BeFalse();
        (await probe.IsUsedAsync(ReferenceItemKind.OperationType, unrelatedType.Id, CancellationToken.None)).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteCurrency_WithSoftDeletedOperation_ReturnsConflictAndKeepsItsRates()
    {
        await AuthenticateAsync();
        var incomeType = await Client.CreateOperationTypeAsync("Income");
        var currency = await Client.CreateCurrencyAsync("PRD", "С операцией");
        var other = await Client.CreateCurrencyAsync("PRE", "Парная");
        await CreateAndSoftDeleteOperationAsync(incomeType.Id, currency.Id);
        await SeedRatesAsync(currency.Id, other.Id);

        var response = await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CountRatesAsync(currency.Id)).Should().Be(new RateCounts(2, 2), "a rejected deletion must not remove rates");
    }

    [Fact]
    public async Task DeleteCurrency_WithLatestAndHistoricalRatesAndNoUsage_ReturnsNoContentAndRemovesRatesOfBothDirections()
    {
        await AuthenticateAsync();
        var doomed = await Client.CreateCurrencyAsync("PRF", "Удаляемая");
        var partner = await Client.CreateCurrencyAsync("PRG", "Парная");
        var bystander = await Client.CreateCurrencyAsync("PRH", "Посторонняя");
        await SeedRatesAsync(doomed.Id, partner.Id);
        await SeedRatesAsync(partner.Id, bystander.Id);

        var response = await Client.DeleteAsync($"/api/v1/currencies/{doomed.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "rates are not usage of a currency (ADR-0002, §4)");
        (await CountRatesAsync(doomed.Id)).Should().Be(new RateCounts(0, 0), "both From and To pairs are removed, latest and historical");
        (await CountRatesAsync(partner.Id)).Should().Be(new RateCounts(2, 2), "only the partner <-> bystander pair remains");
        (await CountRatesAsync(bystander.Id)).Should().Be(new RateCounts(2, 2));
        var list = await Client.GetAsync("/api/v1/currencies");
        (await list.Content.ReadAsStringAsync()).Should().NotContain(doomed.Id.ToString());
    }

    [Fact]
    public async Task DeleteCurrency_WalletDeletedFirstThenCurrency_RemovesRatesLeftByRefresh()
    {
        // Realistic chain: a wallet in the currency existed (rates were fetched for it), the wallet was deleted,
        // and now the currency is deleted together with its rates.
        await AuthenticateAsync();
        var primaryCurrency = await Client.CreateCurrencyAsync("PRI", "Основная");
        var foreign = await Client.CreateCurrencyAsync("PRJ", "Иностранная");
        await Client.CreateWalletAsync(currencyId: primaryCurrency.Id, name: "Основной");
        var foreignWallet = await Client.CreateWalletAsync(currencyId: foreign.Id, name: "Иностранный");
        await SeedRatesAsync(foreign.Id, primaryCurrency.Id);
        (await Client.DeleteAsync($"/api/v1/wallets/{foreignWallet.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var response = await Client.DeleteAsync($"/api/v1/currencies/{foreign.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CountRatesAsync(foreign.Id)).Should().Be(new RateCounts(0, 0));
        (await CountRatesAsync(primaryCurrency.Id)).Should().Be(new RateCounts(0, 0));
    }

    /// <summary>Seeds latest and historical rates for both directions of the pair straight into the exchange_rates schema.</summary>
    private async Task SeedRatesAsync(Guid firstCurrencyId, Guid secondCurrencyId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExchangeRatesDbContext>();
        var first = new CurrencyId(firstCurrencyId);
        var second = new CurrencyId(secondCurrencyId);
        var fetchedAt = DateTimeOffset.UtcNow;
        dbContext.LatestExchangeRates.AddRange(
            ExchangeRateQuote.Create(first, second, new ExchangeRateValue(0.9m), fetchedAt),
            ExchangeRateQuote.Create(second, first, new ExchangeRateValue(1.1m), fetchedAt));
        dbContext.HistoricalExchangeRates.AddRange(
            HistoricalExchangeRate.Create(first, second, Today.AddDays(-5), new ExchangeRateValue(0.8m), fetchedAt),
            HistoricalExchangeRate.Create(second, first, Today.AddDays(-5), new ExchangeRateValue(1.2m), fetchedAt));
        await dbContext.SaveChangesAsync();
    }

    /// <summary>Number of latest/historical rate rows where the currency is From or To (read back in a fresh scope).</summary>
    private async Task<RateCounts> CountRatesAsync(Guid currencyId)
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ExchangeRatesDbContext>();
        var id = new CurrencyId(currencyId);
        var latest = await dbContext.LatestExchangeRates.AsNoTracking()
            .CountAsync(q => q.FromCurrencyId == id || q.ToCurrencyId == id);
        var historical = await dbContext.HistoricalExchangeRates.AsNoTracking()
            .CountAsync(q => q.FromCurrencyId == id || q.ToCurrencyId == id);
        return new RateCounts(latest, historical);
    }

    private readonly record struct RateCounts(int Latest, int Historical);

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

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "валюта используется существующим кошельком (ADR-0002, §4)");
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
