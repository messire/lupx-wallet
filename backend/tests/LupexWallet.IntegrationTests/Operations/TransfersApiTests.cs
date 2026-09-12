using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

// DTO для десериализации ответов Transfers API (docs/api/openapi.yaml).
public sealed record TransferMoneyResponse(string Amount, Guid CurrencyId);
public sealed record TransferResponse(
    Guid Id, Guid SourceWalletId, Guid TargetWalletId, Guid SourceOperationId, Guid TargetOperationId,
    TransferMoneyResponse Amount, DateOnly TransferDate, DateTimeOffset CreatedAt);

/// <summary>
/// Transfers API (docs/api/openapi.yaml, тег Transfers) — UC-16: атомарность пары операций,
/// защита операций-частей перевода от прямого изменения/удаления через /operations,
/// удаление перевода реверсирует обе дельты.
/// </summary>
public sealed class TransfersApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<decimal> GetWalletBalanceAsync(Guid walletId)
    {
        var response = await Client.GetAsync($"/api/v1/wallets/{walletId}");
        response.EnsureSuccessStatusCode();
        var wallet = await response.Content.ReadFromJsonAsync<WalletResponse>();
        return decimal.Parse(wallet!.CurrentBalance.Amount, CultureInfo.InvariantCulture);
    }

    private async Task<(WalletResponse Source, WalletResponse Target)> CreateWalletPairAsync(decimal sourceBalance, decimal targetBalance)
    {
        var currency = await Client.CreateCurrencyAsync("TRF" + Guid.NewGuid().ToString("N")[..5].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var source = await Client.CreateWalletAsync(sourceBalance, name: "Источник", walletTypeId: walletType.Id, currencyId: currency.Id);
        var target = await Client.CreateWalletAsync(targetBalance, name: "Назначение", walletTypeId: walletType.Id, currencyId: currency.Id);
        // CreateTransferCommandHandler ищет активный OperationType с поведением Transfer
        // (IOperationTypeLookup.FindActiveByBehaviorKindCodeAsync) — без него перевод не создать.
        await Client.CreateOperationTypeIdAsync("Transfer");
        return (source, target);
    }

    [Fact]
    public async Task CreateTransfer_MovesAmountAtomicallyBetweenWallets()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);

        var response = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<TransferResponse>();
        body!.Amount.Amount.Should().Be("150");

        (await GetWalletBalanceAsync(source.Id)).Should().Be(350m);
        (await GetWalletBalanceAsync(target.Id)).Should().Be(250m);
    }

    [Fact]
    public async Task CreateTransfer_SameWallet_ReturnsConflict()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(100m);

        var response = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = wallet.Id,
            TargetWalletId = wallet.Id,
            Amount = 10m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task CreateTransfer_MismatchedCurrencies_ReturnsConflict()
    {
        await AuthenticateAsync();
        var walletType = await Client.CreateWalletTypeAsync();
        var currencyA = await Client.CreateCurrencyAsync("AAA", "Валюта A");
        var currencyB = await Client.CreateCurrencyAsync("BBB", "Валюта B");
        var source = await Client.CreateWalletAsync(100m, walletTypeId: walletType.Id, currencyId: currencyA.Id);
        var target = await Client.CreateWalletAsync(100m, walletTypeId: walletType.Id, currencyId: currencyB.Id);

        var response = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 10m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateOperation_PartOfTransfer_ReturnsConflict()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 0m);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 100m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferResponse>())!;
        // Валидный, существующий и активный тип операции — иначе UpdateOperationCommandHandler
        // отклонит запрос как OperationTypeReferenceNotFoundException (400) раньше, чем дойдет
        // до проверки "часть перевода" (EnsureNotPartOfTransfer), и тест перестанет проверять
        // именно этот сценарий.
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        // Прямое редактирование операции — части перевода — через /operations должно быть отклонено
        // (ddd-model.md §5: "редактируется/удаляется только как часть Transfer целиком"). W2.6:
        // унифицировано с DELETE /operations/{id} — оба возвращают 409 (явный catch
        // OperationPartOfTransferException в обоих эндпоинтах, docs/api/openapi.yaml).
        var updateResponse = await Client.PatchAsJsonAsync($"/api/v1/operations/{transfer.SourceOperationId}", new
        {
            WalletId = source.Id,
            OperationTypeId = incomeTypeId,
            Amount = 999m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AdjustmentMode = (string?)null,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateOperation_PartOfTransfer_ChangingWallet_ReturnsConflict()
    {
        // UC-13, решение пользователя от 2026-09-11 — перенос операции на другой кошелек
        // подчиняется тому же ограничению, что и обычное редактирование: операция — часть
        // перевода — не может быть перенесена напрямую через /operations (только через
        // перевод целиком, ddd-model.md §5).
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 0m);
        var otherWallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, currencyId: source.CurrencyId);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 100m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferResponse>())!;
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        var updateResponse = await Client.PatchAsJsonAsync($"/api/v1/operations/{transfer.SourceOperationId}", new
        {
            WalletId = otherWallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 100m,
            OperationDate = DateOnly.FromDateTime(DateTime.UtcNow),
            AdjustmentMode = (string?)null,
        });

        updateResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task DeleteOperation_PartOfTransfer_ReturnsConflict()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 0m);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 100m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferResponse>())!;

        var deleteResponse = await Client.DeleteAsync($"/api/v1/operations/{transfer.SourceOperationId}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "ddd-model.md §5: операцию — часть перевода нельзя удалить напрямую через /operations");
    }

    [Fact]
    public async Task DeleteTransfer_ReversesBothDeltasAndRemovesTransfer()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = DateOnly.FromDateTime(DateTime.UtcNow),
        });
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferResponse>())!;

        var deleteResponse = await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await GetWalletBalanceAsync(source.Id)).Should().Be(500m);
        (await GetWalletBalanceAsync(target.Id)).Should().Be(100m);

        var getSourceOperation = await Client.GetAsync($"/api/v1/operations/{transfer.SourceOperationId}");
        getSourceOperation.StatusCode.Should().Be(HttpStatusCode.NotFound, "удаление перевода атомарно удаляет обе операции");
    }

    [Fact]
    public async Task DeleteTransfer_DatedBeforeToday_ReturnsConflict()
    {
        // ADR-0009, случай b (решение пользователя, 2026-09-11): та же граница "сегодня",
        // что и для одиночной операции, но по TransferDate.
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = yesterday,
        });
        var transfer = (await transferResponse.Content.ReadFromJsonAsync<TransferResponse>())!;

        var deleteResponse = await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "перевод с датой строго раньше сегодня уже считается отраженным в истории баланса");
    }

    [Fact]
    public async Task DeleteTransfer_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.DeleteAsync($"/api/v1/transfers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
