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
    Guid Id, Guid WalletId, Guid? OperationTypeId, OperationMoneyResponse Amount, DateOnly OperationDate,
    string? AdjustmentMode, bool IsCorrection, OperationMoneyResponse? StatedBalance, Guid? TransferId,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, bool IsDeleted, DateTimeOffset? DeletedAt);

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
    public async Task EndToEndScenario_IncomeExpenseDeltaAdjustmentUpdateDelete_KeepsBalanceReconciled()
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

        // Delta-корректировка: баланс должен уменьшиться на 400.
        var bigAdjustment = await CreateOperationAsync(wallet.Id, adjustmentTypeId, -400m, today, "Delta");
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(500m);

        // Delta-корректировка: баланс должен уменьшиться ещё на 50.
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

        _ = bigAdjustment;
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

    // ---- Мягкое удаление с любой датой (ADR-0002, §3) ----

    [Fact]
    public async Task DeleteOperation_DatedToday_ReturnsNoContent()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 10m, DateOnly.FromDateTime(DateTime.UtcNow));

        var response = await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "удаление допустимо с любой датой, в том числе сегодняшней (ADR-0002, §3)");
    }

    [Fact]
    public async Task DeleteOperation_DatedBeforeToday_ReturnsNoContentAndMarksDeleted()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 10m, yesterday);

        var response = await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "удаление допустимо с любой датой (ADR-0002, §3)");
        var getResponse = await Client.GetAsync($"/api/v1/operations/{operation.Id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK, "мягко удалённая операция остаётся доступной");
        var deleted = (await getResponse.Content.ReadFromJsonAsync<OperationResponse>())!;
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
    }

    // ---- Soft delete / restore (ADR-0002, §3) ----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<OperationResponse> GetOperationAsync(Guid id)
    {
        var response = await Client.GetAsync($"/api/v1/operations/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }

    private async Task<HttpResponseMessage> PatchAmountAsync(Guid operationId, Guid walletId, Guid operationTypeId, decimal amount, DateOnly date) =>
        await Client.PatchAsJsonAsync($"/api/v1/operations/{operationId}", new
        {
            WalletId = walletId,
            OperationTypeId = operationTypeId,
            Amount = amount,
            OperationDate = date,
            AdjustmentMode = (string?)null,
        });

    [Fact]
    public async Task DeleteOperation_PastDate_ReturnsNoContentMarksDeletedAndRemovesItsEffectFromBalance()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m);
        var updatedAtBefore = (await GetOperationAsync(operation.Id)).UpdatedAt; // as persisted (timestamptz keeps microseconds)

        var response = await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var deleted = await GetOperationAsync(operation.Id);
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
        deleted.UpdatedAt.Should().Be(updatedAtBefore, "UpdatedAt is the moment of a content edit, not of deletion");
        deleted.Amount.Amount.Should().Be("300", "a soft-deleted row keeps its data");
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m);
    }

    [Fact]
    public async Task DeleteOperation_Expense_ReturnsItsAmountToWalletBalance()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense");
        var expense = await CreateOperationAsync(wallet.Id, expenseTypeId, 250m, Today.AddDays(-2));
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(750m);

        (await Client.DeleteAsync($"/api/v1/operations/{expense.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m);

        (await Client.PostAsync($"/api/v1/operations/{expense.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(750m);
    }

    [Fact]
    public async Task DeleteAndRestoreDeltaAdjustment_UseTheStoredAppliedDelta()
    {
        // Delete/restore must reverse/re-apply exactly the stored applied delta (-500).
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");
        var adjustment = await CreateOperationAsync(wallet.Id, adjustmentTypeId, -500m, Today.AddDays(-2), "Delta");
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(500m);
        await CreateOperationAsync(wallet.Id, incomeTypeId, 100m, Today.AddDays(-1));
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(600m);

        (await Client.DeleteAsync($"/api/v1/operations/{adjustment.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1100m, "the -500 delta is reversed, the later income stays");

        (await Client.PostAsync($"/api/v1/operations/{adjustment.Id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(600m);
    }

    [Fact]
    public async Task CreateOperation_AbsoluteAdjustmentMode_Returns400WithoutChangingBalance()
    {
        // The balance for a date is stated through a separate action (ADR-0003, §6); Absolute is rejected.
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");

        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = adjustmentTypeId,
            Amount = 500m,
            OperationDate = Today,
            AdjustmentMode = "Absolute",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("detail").GetString()
            .Should().Contain("Указать баланс на дату");
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m);
    }

    [Fact]
    public async Task RestoreOperation_Deleted_ReturnsOkWithActiveOperationAndReturnsItsEffect()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        var updatedAtBefore = (await GetOperationAsync(operation.Id)).UpdatedAt;
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        var response = await Client.PostAsync($"/api/v1/operations/{operation.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
        restored.Id.Should().Be(operation.Id);
        restored.IsDeleted.Should().BeFalse();
        restored.DeletedAt.Should().BeNull();
        restored.UpdatedAt.Should().Be(updatedAtBefore);
        (await GetOperationAsync(operation.Id)).IsDeleted.Should().BeFalse();
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m);
    }

    [Fact]
    public async Task DeleteOperation_AlreadyDeleted_ReturnsConflictAndDoesNotChangeBalance()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-1));
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");
        var deletedAt = (await GetOperationAsync(operation.Id)).DeletedAt;

        var response = await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m, "the delta must not be reversed twice");
        (await GetOperationAsync(operation.Id)).DeletedAt.Should().Be(deletedAt);
    }

    [Fact]
    public async Task RestoreOperation_NotDeleted_ReturnsConflictAndDoesNotChangeBalance()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today);

        var response = await Client.PostAsync($"/api/v1/operations/{operation.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m, "the delta must not be applied twice");
    }

    [Fact]
    public async Task RestoreOperation_AfterRestore_SecondRestoreReturnsConflict()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today);
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");
        await Client.PostAsync($"/api/v1/operations/{operation.Id}/restore", null);

        var response = await Client.PostAsync($"/api/v1/operations/{operation.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m);
    }

    [Fact]
    public async Task RestoreOperation_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.PostAsync($"/api/v1/operations/{Guid.NewGuid()}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateOperation_Deleted_ReturnsConflictAndDoesNotChangeBalanceOrOperation()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-1));
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        var response = await PatchAmountAsync(operation.Id, wallet.Id, incomeTypeId, 999m, Today.AddDays(-1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m);
        var unchanged = await GetOperationAsync(operation.Id);
        unchanged.Amount.Amount.Should().Be("300");
        unchanged.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task UpdateOperation_DeletedAndMovedToAnotherWallet_ReturnsConflictAndDoesNotChangeEitherBalance()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("MV" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletA = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, name: "A", currencyId: currency.Id);
        var walletB = await Client.CreateWalletAsync(initialBalanceAmount: 300m, name: "B", currencyId: currency.Id);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(walletA.Id, incomeTypeId, 200m, Today.AddDays(-1));
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");

        var response = await PatchAmountAsync(operation.Id, walletB.Id, incomeTypeId, 200m, Today.AddDays(-1));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(walletA.Id)).Should().Be(1000m);
        (await GetWalletBalanceAsync(walletB.Id)).Should().Be(300m);
        (await GetOperationAsync(operation.Id)).WalletId.Should().Be(walletA.Id);
    }

    [Fact]
    public async Task UpdateOperation_AfterRestore_IsAllowedAgain()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-1));
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");
        await Client.PostAsync($"/api/v1/operations/{operation.Id}/restore", null);

        var response = await PatchAmountAsync(operation.Id, wallet.Id, incomeTypeId, 400m, Today.AddDays(-1));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1400m);
    }

    [Fact]
    public async Task RestoreOperation_OperationTypeDeactivatedAndWalletArchivedMeanwhile_StillReturnsOk()
    {
        // ADR-0002, §3, §6: restoration is always possible — type activity and wallet archival are not re-checked.
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной"); // first wallet is primary; the archived one below must not be
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, name: "Будет в архиве");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operation = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-1));
        await Client.DeleteAsync($"/api/v1/operations/{operation.Id}");
        (await Client.PostAsync($"/api/v1/operation-types/{incomeTypeId}/deactivate", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsync($"/api/v1/wallets/{wallet.Id}/archive", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await Client.PostAsync($"/api/v1/operations/{operation.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m);
    }

    [Fact]
    public async Task ListOperations_IncludesDeletedOnesInTheSamePositionWithIsDeletedFlag()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var first = await CreateOperationAsync(wallet.Id, incomeTypeId, 10m, Today.AddDays(-3));
        var second = await CreateOperationAsync(wallet.Id, incomeTypeId, 20m, Today.AddDays(-2));
        var third = await CreateOperationAsync(wallet.Id, incomeTypeId, 30m, Today.AddDays(-1));
        var idsBefore = (await ListOperationsAsync(wallet.Id)).Select(o => o.Id).ToList();

        await Client.DeleteAsync($"/api/v1/operations/{second.Id}");

        var after = await ListOperationsAsync(wallet.Id);
        after.Select(o => o.Id).Should().Equal(idsBefore, "the order is unchanged and deleted rows are not hidden");
        after.Should().HaveCount(3);
        after.Single(o => o.Id == second.Id).IsDeleted.Should().BeTrue();
        after.Single(o => o.Id == second.Id).DeletedAt.Should().NotBeNull();
        after.Where(o => o.Id != second.Id).Should().OnlyContain(o => !o.IsDeleted && o.DeletedAt == null);
        _ = first;
        _ = third;
    }

    [Fact]
    public async Task ListOperations_CursorPagination_ReturnsDeletedOperationsWithoutGapsOrDuplicates()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var ids = new List<Guid>();
        for (var i = 1; i <= 5; i++)
        {
            ids.Add((await CreateOperationAsync(wallet.Id, incomeTypeId, i * 10m, Today.AddDays(-i))).Id);
        }

        await Client.DeleteAsync($"/api/v1/operations/{ids[1]}");
        await Client.DeleteAsync($"/api/v1/operations/{ids[3]}");

        var collected = new List<OperationResponse>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/operations?walletId={wallet.Id}&limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var page = (await (await Client.GetAsync(url)).Content.ReadFromJsonAsync<OperationPageResponse>())!;
            collected.AddRange(page.Data);
            cursor = page.Pagination.HasMore ? page.Pagination.NextCursor : null;
        }
        while (cursor is not null);

        collected.Select(o => o.Id).Should().BeEquivalentTo(ids);
        collected.Select(o => o.Id).Should().OnlyHaveUniqueItems();
        collected.Where(o => o.IsDeleted).Select(o => o.Id).Should().BeEquivalentTo([ids[1], ids[3]]);
    }

    private async Task<IReadOnlyList<OperationResponse>> ListOperationsAsync(Guid walletId)
    {
        var response = await Client.GetAsync($"/api/v1/operations?walletId={walletId}&limit=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationPageResponse>())!.Data;
    }

    private sealed record OperationPageMeta(string? NextCursor, bool HasMore);
    private sealed record OperationPageResponse(IReadOnlyList<OperationResponse> Data, OperationPageMeta Pagination);

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
