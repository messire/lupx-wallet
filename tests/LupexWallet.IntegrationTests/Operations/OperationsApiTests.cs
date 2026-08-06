using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

// DTO для десериализации ответов Operations API (docs/api/openapi.yaml).
public sealed record OperationMoneyResponse(string Amount, Guid CurrencyId);
public sealed record OperationResponse(
    Guid Id, Guid WalletId, Guid OperationTypeId, OperationMoneyResponse Amount, DateOnly OperationDate,
    string? AdjustmentMode, Guid? TransferId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>
/// Operations API (docs/api/openapi.yaml, тег Operations) — UC-11/UC-12/UC-13/UC-14/UC-24.
/// Сквозной сценарий доход→расход→обе корректировки→изменение→удаление со сверкой баланса
/// на каждом шаге воспроизводит вживую проверенный сценарий из docs/PROGRESS.md.
/// </summary>
public sealed class OperationsApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<decimal> GetWalletBalanceAsync(Guid walletId)
    {
        var response = await Client.GetAsync($"/api/v1/wallets/{walletId}");
        response.EnsureSuccessStatusCode();
        var wallet = await response.Content.ReadFromJsonAsync<WalletResponse>();
        return decimal.Parse(wallet!.CurrentBalance.Amount, CultureInfo.InvariantCulture);
    }

    private async Task<OperationResponse> CreateOperationAsync(
        Guid walletId, Guid operationTypeId, decimal amount, DateOnly date, string? adjustmentMode = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = walletId,
            OperationTypeId = operationTypeId,
            Amount = amount,
            OperationDate = date,
            AdjustmentMode = adjustmentMode,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }

    [Fact]
    public async Task EndToEndScenario_IncomeExpenseBothAdjustmentModesUpdateDelete_KeepsBalanceReconciled()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense");
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");

        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m);

        var income = await CreateOperationAsync(wallet.Id, incomeTypeId, 200m, today);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1200m);

        var expense = await CreateOperationAsync(wallet.Id, expenseTypeId, 300m, today);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(900m);

        // Absolute-корректировка: баланс должен стать ровно 500.
        var absoluteAdjustment = await CreateOperationAsync(wallet.Id, adjustmentTypeId, 500m, today, "Absolute");
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(500m);

        // Delta-корректировка: baланс должен уменьшиться на 50.
        var deltaAdjustment = await CreateOperationAsync(wallet.Id, adjustmentTypeId, -50m, today, "Delta");
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(450m);

        // Изменение дохода со 200 на 350 -> баланс увеличивается на 150.
        var updateResponse = await Client.PatchAsJsonAsync($"/api/v1/operations/{income.Id}", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 350m,
            OperationDate = today,
            AdjustmentMode = (string?)null,
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(600m);

        // Удаление расхода (300) -> баланс возвращает эти 300.
        var deleteResponse = await Client.DeleteAsync($"/api/v1/operations/{expense.Id}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(900m);

        _ = absoluteAdjustment;
        _ = deltaAdjustment;
    }

    [Fact]
    public async Task UpdateOperation_ChangingWallet_MovesOperationAndReconcilesBothBalances()
    {
        // UC-13, решение пользователя от 2026-09-11: смена кошелька операции при
        // редактировании — перенос, эквивалентный удалению со старого кошелька + созданию
        // на новом (Operations.Application.UpdateOperationCommand).
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("MV" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletA = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, name: "A", currencyId: currency.Id);
        var walletB = await Client.CreateWalletAsync(initialBalanceAmount: 300m, name: "B", currencyId: currency.Id);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var operation = await CreateOperationAsync(walletA.Id, incomeTypeId, 200m, today);
        (await GetWalletBalanceAsync(walletA.Id)).Should().Be(1200m);
        (await GetWalletBalanceAsync(walletB.Id)).Should().Be(300m);

        var updateResponse = await Client.PatchAsJsonAsync($"/api/v1/operations/{operation.Id}", new
        {
            WalletId = walletB.Id,
            OperationTypeId = incomeTypeId,
            Amount = 200m,
            OperationDate = today,
            AdjustmentMode = (string?)null,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResponse.Content.ReadFromJsonAsync<OperationResponse>();
        updated!.WalletId.Should().Be(walletB.Id);

        // Кошелек A потерял вклад операции (реверс), кошелек B — приобрел.
        (await GetWalletBalanceAsync(walletA.Id)).Should().Be(1000m);
        (await GetWalletBalanceAsync(walletB.Id)).Should().Be(500m);
    }

    [Fact]
    public async Task CreateOperation_WithTransferBehaviorKindDirectly_ReturnsBadRequest()
    {
        // Регресс: раньше падало 500 вместо доменного исключения (docs/PROGRESS.md).
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var transferTypeId = await Client.CreateOperationTypeIdAsync("Transfer");

        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = transferTypeId,
            Amount = 10m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AdjustmentMode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOperation_WithDateBeforeAccountingStartDate_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var accountingStartDate = DateOnly.FromDateTime(DateTime.UtcNow);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 10m,
            OperationDate = accountingStartDate.AddDays(-1),
            AdjustmentMode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOperation_WithDateAfterToday_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 10m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1),
            AdjustmentMode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateOperation_Income_WithNonPositiveAmount_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 0m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AdjustmentMode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DeleteOperation_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.DeleteAsync($"/api/v1/operations/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- ADR-0009, случай b (решение пользователя, 2026-09-11): граница "история" — сегодня ----

    [Fact]
    public async Task DeleteOperation_DatedToday_ReturnsNoContent()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 10m, DateOnly.FromDateTime(DateTime.UtcNow));

        var response = await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "операция, датированная сегодняшним днем, всегда может быть удалена");
    }

    [Fact]
    public async Task DeleteOperation_DatedBeforeToday_ReturnsConflict()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 10m, yesterday);

        var response = await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "операция с датой строго раньше сегодня уже считается отраженной в истории баланса (ADR-0009, случай b)");
    }

    [Fact]
    public async Task GetOperation_AfterCreate_ReturnsIt()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var created = await CreateOperationAsync(wallet.Id, incomeTypeId, 25m, DateOnly.FromDateTime(DateTime.UtcNow));

        var response = await Client.GetAsync($"/api/v1/operations/{created.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<OperationResponse>();
        body!.Id.Should().Be(created.Id);
        body.Amount.Amount.Should().Be("25");
    }
}
