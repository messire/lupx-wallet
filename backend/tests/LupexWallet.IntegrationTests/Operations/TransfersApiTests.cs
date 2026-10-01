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
    TransferMoneyResponse Amount, DateOnly TransferDate, DateTimeOffset CreatedAt,
    bool IsDeleted, DateTimeOffset? DeletedAt);

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
    public async Task DeleteTransfer_ReversesBothDeltasAndSoftDeletesTransfer()
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
        getSourceOperation.StatusCode.Should().Be(HttpStatusCode.OK, "удаление перевода мягкое — обе операции остаются");
        var sourceLeg = (await getSourceOperation.Content.ReadFromJsonAsync<OperationResponse>())!;
        sourceLeg.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteTransfer_DatedBeforeToday_ReturnsNoContentAndMarksDeleted()
    {
        // ADR-0002, §3: прежнее правило «граница — сегодня» отменено, удаление допустимо с любой датой.
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

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent, "удаление допустимо с любой датой (ADR-0002, §3)");
        var getResponse = await Client.GetAsync($"/api/v1/transfers/{transfer.Id}");
        var deleted = (await getResponse.Content.ReadFromJsonAsync<TransferResponse>())!;
        deleted.IsDeleted.Should().BeTrue();
        deleted.DeletedAt.Should().NotBeNull();
    }

    // ---- Soft delete / restore of a transfer as a whole (ADR-0002, §3) ----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<TransferResponse> CreateTransferAsync(Guid sourceId, Guid targetId, decimal amount, DateOnly date)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = sourceId,
            TargetWalletId = targetId,
            Amount = amount,
            TransferDate = date,
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
    }

    private async Task<TransferResponse> GetTransferAsync(Guid id)
    {
        var response = await Client.GetAsync($"/api/v1/transfers/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
    }

    private async Task<OperationResponse> GetOperationAsync(Guid id)
    {
        var response = await Client.GetAsync($"/api/v1/operations/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }

    [Fact]
    public async Task DeleteTransfer_PastDate_MarksTransferAndBothLegsDeletedWithTheSameDeletedAt()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today.AddDays(-4));

        var response = await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var deletedTransfer = await GetTransferAsync(transfer.Id);
        var sourceLeg = await GetOperationAsync(transfer.SourceOperationId);
        var targetLeg = await GetOperationAsync(transfer.TargetOperationId);
        deletedTransfer.IsDeleted.Should().BeTrue();
        deletedTransfer.DeletedAt.Should().NotBeNull();
        sourceLeg.IsDeleted.Should().BeTrue();
        targetLeg.IsDeleted.Should().BeTrue();
        sourceLeg.DeletedAt.Should().Be(deletedTransfer.DeletedAt);
        targetLeg.DeletedAt.Should().Be(deletedTransfer.DeletedAt);
        (await GetWalletBalanceAsync(source.Id)).Should().Be(500m);
        (await GetWalletBalanceAsync(target.Id)).Should().Be(100m);
    }

    [Fact]
    public async Task RestoreTransfer_Deleted_ReturnsOkRestoresBothLegsAndBothBalances()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today.AddDays(-4));
        await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");

        var response = await Client.PostAsync($"/api/v1/transfers/{transfer.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var restored = (await response.Content.ReadFromJsonAsync<TransferResponse>())!;
        restored.Id.Should().Be(transfer.Id);
        restored.IsDeleted.Should().BeFalse();
        restored.DeletedAt.Should().BeNull();
        (await GetOperationAsync(transfer.SourceOperationId)).IsDeleted.Should().BeFalse();
        (await GetOperationAsync(transfer.TargetOperationId)).IsDeleted.Should().BeFalse();
        (await GetWalletBalanceAsync(source.Id)).Should().Be(350m);
        (await GetWalletBalanceAsync(target.Id)).Should().Be(250m);
    }

    [Fact]
    public async Task DeleteTransfer_AlreadyDeleted_ReturnsConflictAndDoesNotReverseTwice()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today);
        await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");
        var deletedAt = (await GetTransferAsync(transfer.Id)).DeletedAt;

        var response = await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(source.Id)).Should().Be(500m);
        (await GetWalletBalanceAsync(target.Id)).Should().Be(100m);
        (await GetTransferAsync(transfer.Id)).DeletedAt.Should().Be(deletedAt);
    }

    [Fact]
    public async Task RestoreTransfer_NotDeleted_ReturnsConflictAndDoesNotApplyTwice()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today);

        var response = await Client.PostAsync($"/api/v1/transfers/{transfer.Id}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(source.Id)).Should().Be(350m);
        (await GetWalletBalanceAsync(target.Id)).Should().Be(250m);
    }

    [Fact]
    public async Task RestoreTransfer_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.PostAsync($"/api/v1/transfers/{Guid.NewGuid()}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteAndRestoreLegViaOperations_AfterTransferDeleted_ReturnConflictAndLegsStayDeleted()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today.AddDays(-2));
        await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");

        var restoreSource = await Client.PostAsync($"/api/v1/operations/{transfer.SourceOperationId}/restore", null);
        var restoreTarget = await Client.PostAsync($"/api/v1/operations/{transfer.TargetOperationId}/restore", null);
        var deleteSource = await Client.DeleteAsync($"/api/v1/operations/{transfer.SourceOperationId}");

        restoreSource.StatusCode.Should().Be(HttpStatusCode.Conflict, "a leg is restored only together with its transfer (ADR-0002, §3)");
        restoreTarget.StatusCode.Should().Be(HttpStatusCode.Conflict);
        deleteSource.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetOperationAsync(transfer.SourceOperationId)).IsDeleted.Should().BeTrue();
        (await GetOperationAsync(transfer.TargetOperationId)).IsDeleted.Should().BeTrue();
        (await GetWalletBalanceAsync(source.Id)).Should().Be(500m);
    }

    [Fact]
    public async Task RestoreLegViaOperations_TransferActive_ReturnsConflict()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today);

        var response = await Client.PostAsync($"/api/v1/operations/{transfer.TargetOperationId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateLegViaOperations_AfterTransferDeleted_ReturnsConflict()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var transfer = await CreateTransferAsync(source.Id, target.Id, 150m, Today);
        await Client.DeleteAsync($"/api/v1/transfers/{transfer.Id}");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        var response = await Client.PatchAsJsonAsync($"/api/v1/operations/{transfer.SourceOperationId}", new
        {
            WalletId = source.Id,
            OperationTypeId = incomeTypeId,
            Amount = 1m,
            OperationDate = Today,
            AdjustmentMode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetWalletBalanceAsync(source.Id)).Should().Be(500m);
    }

    [Fact]
    public async Task ListTransfers_IncludesDeletedTransferWithIsDeletedFlag()
    {
        await AuthenticateAsync();
        var (source, target) = await CreateWalletPairAsync(sourceBalance: 500m, targetBalance: 100m);
        var kept = await CreateTransferAsync(source.Id, target.Id, 10m, Today.AddDays(-2));
        var removed = await CreateTransferAsync(source.Id, target.Id, 20m, Today.AddDays(-1));
        var idsBefore = (await ListTransfersAsync()).Select(t => t.Id).ToList();
        await Client.DeleteAsync($"/api/v1/transfers/{removed.Id}");

        var after = await ListTransfersAsync();

        after.Select(t => t.Id).Should().Equal(idsBefore, "deleted transfers are not hidden and keep their place");
        after.Single(t => t.Id == removed.Id).IsDeleted.Should().BeTrue();
        after.Single(t => t.Id == removed.Id).DeletedAt.Should().NotBeNull();
        after.Single(t => t.Id == kept.Id).IsDeleted.Should().BeFalse();
    }

    private async Task<IReadOnlyList<TransferResponse>> ListTransfersAsync()
    {
        var response = await Client.GetAsync("/api/v1/transfers?limit=100");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<TransferPageResponse>())!.Data;
    }

    private sealed record TransferPageMeta(string? NextCursor, bool HasMore);
    private sealed record TransferPageResponse(IReadOnlyList<TransferResponse> Data, TransferPageMeta Pagination);

    [Fact]
    public async Task DeleteTransfer_UnknownId_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.DeleteAsync($"/api/v1/transfers/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
