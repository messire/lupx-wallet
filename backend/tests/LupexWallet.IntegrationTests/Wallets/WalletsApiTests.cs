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

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "у кошелька уже есть история операций (ADR-0002, §2)");
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

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "у кошелька уже есть операции (Q15, ADR-0002, §2)");
    }

    // ---- Snapshots are derived data, not history (ADR-0002, §2) ----

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<long> CountSnapshotsAsync(Guid walletId)
    {
        await using var connection = await Fixture.OpenConnectionAsync();
        return await connection.CountAsync(
            "SELECT count(*) FROM balance_history.balance_snapshots WHERE wallet_id = @walletId", ("walletId", walletId));
    }

    [Fact]
    public async Task DeleteWallet_WithSnapshotsButNoOperations_ReturnsNoContentRemovesSnapshotsAndAuditsEachOne()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var startDate = Today.AddDays(-3);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: startDate, name: "Со слепками");
        await Fixture.BackfillSnapshotsAsync(wallet.Id);
        (await CountSnapshotsAsync(wallet.Id)).Should().Be(4, "snapshots for start date..today inclusive");
        (await Client.GetSnapshotsAsync(wallet.Id, startDate, Today)).Should().HaveCount(4);

        var response = await Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, "snapshots are not wallet history");
        (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}/balance-history?from={startDate:yyyy-MM-dd}&to={Today:yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CountSnapshotsAsync(wallet.Id)).Should().Be(0, "snapshots are removed in the same transaction as the wallet");

        var snapshotAudit = await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id);
        snapshotAudit.Count(e => e.Action == "SnapshotCreated").Should().Be(4);
        var deletedEntries = snapshotAudit.Where(e => e.Action == "SnapshotDeleted").ToList();
        deletedEntries.Should().HaveCount(4, "one SnapshotDeleted record per removed snapshot");
        deletedEntries.Select(e => e.Changes.Single(c => c.Field == "SnapshotDate").OldValue)
            .Should().BeEquivalentTo(Enumerable.Range(0, 4).Select(i => startDate.AddDays(i).ToString("yyyy-MM-dd")));
        deletedEntries.Should().OnlyContain(e =>
            e.Changes.Single(c => c.Field == "Balance").OldValue == "100" && e.Changes.Single(c => c.Field == "Balance").NewValue == null);
        (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Should().Contain(e => e.Action == "Deleted");
    }

    [Fact]
    public async Task DeleteWallet_OnlySoftDeletedOperation_ReturnsConflictKeepsSnapshotsAndArchiveStillWorks()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-3), name: "С удалённой");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 10m, Today.AddDays(-2));
        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var snapshotsBefore = await CountSnapshotsAsync(wallet.Id);
        snapshotsBefore.Should().BeGreaterThan(0);

        var deleteResponse = await Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}");

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.Conflict, "history includes soft-deleted operations (ADR-0002, §2)");
        (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CountSnapshotsAsync(wallet.Id)).Should().Be(snapshotsBefore, "a rejected deletion must not touch the snapshots");

        var archiveResponse = await Client.PostAsync($"/api/v1/wallets/{wallet.Id}/archive", content: null);

        archiveResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        (await archiveResponse.Content.ReadFromJsonAsync<WalletResponse>())!.IsArchived.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteWallet_WithoutSnapshotsAndOperations_StillReturnsNoContent()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(name: "Основной");
        var wallet = await Client.CreateWalletAsync(accountingStartDate: Today.AddDays(-3), name: "Пустой");

        var response = await Client.DeleteAsync($"/api/v1/wallets/{wallet.Id}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id)).Should().BeEmpty();
    }

    [Fact]
    public async Task ChangeWalletCurrency_WithSnapshotsButNoOperations_RebuildsSnapshotsInNewCurrencyAndAuditsBothSides()
    {
        await AuthenticateAsync();
        var startDate = Today.AddDays(-3);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: startDate, name: "Со слепками");
        await Fixture.BackfillSnapshotsAsync(wallet.Id);
        var newCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "snapshots are not history (ADR-0002, §2)");
        var snapshots = await Client.GetSnapshotsAsync(wallet.Id, startDate, Today);
        snapshots.Should().HaveCount(4, "rebuilt from AccountingStartDate through today");
        snapshots.Select(s => s.Date).Should().BeEquivalentTo(Enumerable.Range(0, 4).Select(i => startDate.AddDays(i)));
        snapshots.Should().OnlyContain(s => s.Balance.CurrencyId == newCurrency.Id && s.Balance.Amount == "100");
        (await CountSnapshotsAsync(wallet.Id)).Should().Be(4, "old snapshots are removed, not duplicated");

        var audit = await Client.GetAuditEntriesAsync("BalanceSnapshot", wallet.Id);
        audit.Count(e => e.Action == "SnapshotDeleted").Should().Be(4);
        audit.Count(e => e.Action == "SnapshotCreated").Should().Be(8, "4 original + 4 rebuilt in the new currency");
        (await Client.GetAuditEntriesAsync("Wallet", wallet.Id)).Should().Contain(e => e.Action == "CurrencyChanged");
    }

    [Fact]
    public async Task ChangeWalletCurrency_WithSnapshots_ThenCreateOperationReturnsCreatedAndSnapshotsFollowIt()
    {
        // Regression: BalanceSnapshot.UpdateBalance rejects a currency change, so without the rebuild the
        // first operation in such a wallet would fail inside the command transaction.
        await AuthenticateAsync();
        var startDate = Today.AddDays(-3);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: startDate, name: "Со слепками");
        await Fixture.BackfillSnapshotsAsync(wallet.Id);
        var newCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");
        (await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");

        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 50m,
            OperationDate = Today.AddDays(-2),
            AdjustmentMode = (string?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var snapshots = (await Client.GetSnapshotsAsync(wallet.Id, startDate, Today)).ToDictionary(s => s.Date);
        snapshots[Today.AddDays(-3)].Balance.Amount.Should().Be("100");
        snapshots[Today.AddDays(-2)].Balance.Amount.Should().Be("150");
        snapshots[Today].Balance.Amount.Should().Be("150");
        snapshots.Values.Should().OnlyContain(s => s.Balance.CurrencyId == newCurrency.Id);

        // The scheduled/startup pass (backfill command) must not fail on the rebuilt history either.
        var act = () => Fixture.BackfillSnapshotsAsync(wallet.Id);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ChangeWalletCurrency_OnlySoftDeletedOperation_ReturnsConflictAndKeepsSnapshots()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-3), name: "С удалённой");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 10m, Today.AddDays(-2));
        await Client.DeleteAsync($"/api/v1/operations/{operationId}");
        var snapshotsBefore = await Client.GetSnapshotsAsync(wallet.Id, Today.AddDays(-3), Today);
        var newCurrency = await Client.CreateCurrencyAsync("EUR", "Евро");

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{wallet.Id}/currency", new { CurrencyId = newCurrency.Id });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "soft-deleted operations are history (ADR-0002, §2)");
        (await Client.GetSnapshotsAsync(wallet.Id, Today.AddDays(-3), Today)).Should().BeEquivalentTo(snapshotsBefore);
        var walletAfter = (await (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}")).Content.ReadFromJsonAsync<WalletResponse>())!;
        walletAfter.CurrencyId.Should().Be(wallet.CurrencyId);
    }

    private sealed record CursorPageMeta(string? NextCursor, bool HasMore);
    private sealed record WalletPageResponse(IReadOnlyList<WalletResponse> Data, CursorPageMeta Pagination);
}
