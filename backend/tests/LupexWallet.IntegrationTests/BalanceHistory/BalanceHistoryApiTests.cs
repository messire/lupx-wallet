using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.BalanceHistory;

// DTO для десериализации ответов BalanceHistory API (docs/api/openapi.yaml).
public sealed record BalanceMoneyResponse(string Amount, Guid CurrencyId);
public sealed record BalanceSnapshotResponse(Guid WalletId, DateOnly Date, BalanceMoneyResponse Balance);
public sealed record BalanceCursorPageMeta(string? NextCursor, bool HasMore);
public sealed record BalanceSnapshotPageResponse(IReadOnlyList<BalanceSnapshotResponse> Data, BalanceCursorPageMeta Pagination);

/// <summary>
/// BalanceHistory API (docs/api/openapi.yaml, тег BalanceHistory) — GET /balance,
/// GET /balance-history (курсорная пагинация, from&gt;to → 400), каскадный пересчет
/// слепков при создании/изменении/удалении операции задним числом (ADR-0003).
/// Пересчет запускается синхронно, в рамках той же транзакции, что и команда Operations
/// (подписчик на доменные события, post-save диспетчеризация) — не требует отдельного
/// ожидания фоновой задачи.
/// </summary>
public sealed class BalanceHistoryApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<Guid> CreateOperationAsync(Guid walletId, Guid operationTypeId, decimal amount, DateOnly date)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = walletId,
            OperationTypeId = operationTypeId,
            Amount = amount,
            OperationDate = date,
            AdjustmentMode = (string?)null,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task<BalanceSnapshotPageResponse> GetHistoryAsync(Guid walletId, DateOnly from, DateOnly to, string? cursor = null, int limit = 20)
    {
        var url = $"/api/v1/wallets/{walletId}/balance-history?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&limit={limit}";
        if (cursor is not null)
        {
            url += $"&cursor={Uri.EscapeDataString(cursor)}";
        }

        var response = await Client.GetAsync(url);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<BalanceSnapshotPageResponse>())!;
    }

    [Fact]
    public async Task CreateOperation_CascadesSnapshotsFromOperationDateThroughToday()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationDate = Today.AddDays(-3);

        await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, operationDate);

        var page = await GetHistoryAsync(wallet.Id, accountingStartDate, Today, limit: 100);

        // Слепки материализуются только с даты операции (не со всего accountingStartDate) —
        // ADR-0003: каскадный пересчет диапазона [fromDate..сегодня], fromDate = дата операции.
        page.Data.Should().HaveCount((Today.DayNumber - operationDate.DayNumber) + 1);
        page.Data.Should().OnlyContain(s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture) == 1300m);
        page.Data.Select(s => s.Date).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task CreateRetroactiveOperation_CascadesAndUpdatesAlreadyMaterializedSnapshots()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense");

        // Операция A на today-3: слепки today-3..today = 1300.
        await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        // Ретроактивная операция B на today-7 (раньше A) — должна каскадно пересчитать
        // today-7..today, включая уже материализованные today-3..today.
        await CreateOperationAsync(wallet.Id, expenseTypeId, 100m, Today.AddDays(-7));

        var page = await GetHistoryAsync(wallet.Id, accountingStartDate, Today, limit: 100);
        var byDate = page.Data.ToDictionary(s => s.Date, s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture));

        byDate.Should().HaveCount(8); // today-7 .. today включительно
        byDate[Today.AddDays(-7)].Should().Be(900m, "1000 - 100 (расход B)");
        byDate[Today.AddDays(-6)].Should().Be(900m);
        byDate[Today.AddDays(-4)].Should().Be(900m, "до операции A баланс не меняется");
        byDate[Today.AddDays(-3)].Should().Be(1200m, "900 + 300 (доход A)");
        byDate[Today].Should().Be(1200m);

        var walletResponse = await Client.GetAsync($"/api/v1/wallets/{wallet.Id}");
        var walletBody = await walletResponse.Content.ReadFromJsonAsync<WalletResponse>();
        decimal.Parse(walletBody!.CurrentBalance.Amount, CultureInfo.InvariantCulture).Should().Be(1200m, "CurrentBalance согласован с историей на сегодня");
    }

    [Fact]
    public async Task UpdateOperationDate_CascadesFromEarliestOfOldAndNewDate()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await CreateOperationAsync(wallet.Id, incomeTypeId, 200m, Today.AddDays(-2));

        var earlierDate = Today.AddDays(-6);
        var updateResponse = await Client.PatchAsJsonAsync($"/api/v1/operations/{operationId}", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 200m,
            OperationDate = earlierDate,
            AdjustmentMode = (string?)null,
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await GetHistoryAsync(wallet.Id, accountingStartDate, Today, limit: 100);
        page.Data.Should().Contain(s => s.Date == earlierDate);
        page.Data.Should().OnlyContain(s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture) == 1200m);
    }

    [Fact]
    public async Task UpdateOperationWallet_CascadesRecalculationOnBothOldAndNewWallet()
    {
        // UC-13, решение пользователя от 2026-09-11: перенос операции на другой кошелек
        // должен пересчитать историю ОБОИХ кошельков (BalanceHistory.Infrastructure.
        // OperationUpdatedHandler, PreviousWalletId).
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("MV" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var accountingStartDate = Today.AddDays(-10);
        var walletA = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate, name: "A", currencyId: currency.Id);
        var walletB = await Client.CreateWalletAsync(initialBalanceAmount: 300m, accountingStartDate: accountingStartDate, name: "B", currencyId: currency.Id);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationDate = Today.AddDays(-3);

        var operationId = await CreateOperationAsync(walletA.Id, incomeTypeId, 200m, operationDate);

        var updateResponse = await Client.PatchAsJsonAsync($"/api/v1/operations/{operationId}", new
        {
            WalletId = walletB.Id,
            OperationTypeId = incomeTypeId,
            Amount = 200m,
            OperationDate = operationDate,
            AdjustmentMode = (string?)null,
        });
        updateResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var pageA = await GetHistoryAsync(walletA.Id, accountingStartDate, Today, limit: 100);
        pageA.Data.Should().OnlyContain(s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture) == 1000m, "кошелек-источник больше не учитывает перенесенную операцию");

        var pageB = await GetHistoryAsync(walletB.Id, accountingStartDate, Today, limit: 100);
        pageB.Data.Should().Contain(s => s.Date == operationDate);
        pageB.Data.Should().OnlyContain(s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture) == 500m, "кошелек-получатель учитывает операцию с даты ее OperationDate");
    }

    // ---- Soft delete / restore recalculates history from the operation date (ADR-0002, §3; ADR-0003, §2) ----

    private static decimal Amount(BalanceSnapshotResponse snapshot) => decimal.Parse(snapshot.Balance.Amount, CultureInfo.InvariantCulture);

    [Fact]
    public async Task DeleteOperation_PastDate_RecalculatesSnapshotsFromItsDateWithoutIt()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await CreateOperationAsync(wallet.Id, incomeTypeId, 100m, Today.AddDays(-6));
        var laterId = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        var before = (await Client.GetSnapshotsAsync(wallet.Id, accountingStartDate, Today)).ToDictionary(s => s.Date, Amount);
        before[Today.AddDays(-4)].Should().Be(1100m);
        before[Today].Should().Be(1400m);

        (await Client.DeleteAsync($"/api/v1/operations/{laterId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var after = (await Client.GetSnapshotsAsync(wallet.Id, accountingStartDate, Today)).ToDictionary(s => s.Date, Amount);
        after.Keys.Should().BeEquivalentTo(before.Keys, "snapshots are recalculated in place, not removed");
        after[Today.AddDays(-6)].Should().Be(1100m);
        after[Today.AddDays(-4)].Should().Be(1100m, "the deleted operation was later than this date");
        after[Today.AddDays(-3)].Should().Be(1100m, "from the deleted operation's date the snapshots drop its effect");
        after[Today].Should().Be(1100m);
    }

    [Fact]
    public async Task RestoreOperation_PastDate_RecalculatesSnapshotsBackToTheirPreviousValues()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await CreateOperationAsync(wallet.Id, incomeTypeId, 100m, Today.AddDays(-6));
        var operationId = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        var before = (await Client.GetSnapshotsAsync(wallet.Id, accountingStartDate, Today)).ToDictionary(s => s.Date, Amount);
        await Client.DeleteAsync($"/api/v1/operations/{operationId}");

        (await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        var after = (await Client.GetSnapshotsAsync(wallet.Id, accountingStartDate, Today)).ToDictionary(s => s.Date, Amount);
        after.Should().BeEquivalentTo(before);
        after[Today.AddDays(-4)].Should().Be(1100m);
        after[Today.AddDays(-3)].Should().Be(1400m);
        after[Today].Should().Be(1400m);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(1400m, "CurrentBalance agrees with the snapshot for today");
    }

    [Fact]
    public async Task GetWalletBalance_OnDate_ExcludesDeletedOperationAndCountsItAgainAfterRestore()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await CreateOperationAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, Today.AddDays(-3))).Should().Be(1300m);

        await Client.DeleteAsync($"/api/v1/operations/{operationId}");

        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, Today.AddDays(-3))).Should().Be(1000m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, Today)).Should().Be(1000m);

        await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null);

        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, Today.AddDays(-4))).Should().Be(1000m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, Today.AddDays(-3))).Should().Be(1300m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, Today)).Should().Be(1300m);
    }

    [Fact]
    public async Task GetWalletBalance_OnDate_WithDeletedOperationOnTheSameDate_CountsOnlyActiveOnes()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var date = Today.AddDays(-2);
        await CreateOperationAsync(wallet.Id, incomeTypeId, 100m, date);
        var deletedId = await CreateOperationAsync(wallet.Id, incomeTypeId, 50m, date);

        await Client.DeleteAsync($"/api/v1/operations/{deletedId}");

        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, date)).Should().Be(1100m);
    }

    [Fact]
    public async Task DeleteAndRestoreTransfer_PastDate_RecalculatesSnapshotsOfBothWallets()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var currency = await Client.CreateCurrencyAsync("TH" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var source = await Client.CreateWalletAsync(1000m, accountingStartDate, "Источник", walletType.Id, currency.Id);
        var target = await Client.CreateWalletAsync(200m, accountingStartDate, "Назначение", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        var transferDate = Today.AddDays(-4);
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = transferDate,
        });
        transferResponse.EnsureSuccessStatusCode();
        var transferId = (await transferResponse.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
        (await Client.GetSnapshotsAsync(source.Id, transferDate, Today)).Should().OnlyContain(s => Amount(s) == 850m);
        (await Client.GetSnapshotsAsync(target.Id, transferDate, Today)).Should().OnlyContain(s => Amount(s) == 350m);

        (await Client.DeleteAsync($"/api/v1/transfers/{transferId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var sourceAfterDelete = await Client.GetSnapshotsAsync(source.Id, transferDate, Today);
        var targetAfterDelete = await Client.GetSnapshotsAsync(target.Id, transferDate, Today);
        sourceAfterDelete.Should().HaveCount((Today.DayNumber - transferDate.DayNumber) + 1).And.OnlyContain(s => Amount(s) == 1000m);
        targetAfterDelete.Should().HaveCount((Today.DayNumber - transferDate.DayNumber) + 1).And.OnlyContain(s => Amount(s) == 200m);

        (await Client.PostAsync($"/api/v1/transfers/{transferId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Client.GetSnapshotsAsync(source.Id, transferDate, Today)).Should().OnlyContain(s => Amount(s) == 850m);
        (await Client.GetSnapshotsAsync(target.Id, transferDate, Today)).Should().OnlyContain(s => Amount(s) == 350m);
    }

    [Fact]
    public async Task GetBalanceHistory_FromGreaterThanTo_ReturnsBadRequest()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m);

        var response = await Client.GetAsync(
            $"/api/v1/wallets/{wallet.Id}/balance-history?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetBalanceHistory_UnknownWallet_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.GetAsync(
            $"/api/v1/wallets/{Guid.NewGuid()}/balance-history?from={Today.AddDays(-1):yyyy-MM-dd}&to={Today:yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetBalanceHistory_CursorPagination_ReturnsAllSnapshotsAcrossPagesWithoutDuplicates()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        // Операция на самую раннюю дату диапазона материализует весь диапазон [accountingStartDate..today].
        await CreateOperationAsync(wallet.Id, incomeTypeId, 10m, accountingStartDate);

        var expectedCount = (Today.DayNumber - accountingStartDate.DayNumber) + 1;
        var collected = new List<DateOnly>();
        string? cursor = null;
        do
        {
            var page = await GetHistoryAsync(wallet.Id, accountingStartDate, Today, cursor, limit: 3);
            collected.AddRange(page.Data.Select(s => s.Date));
            cursor = page.Pagination.HasMore ? page.Pagination.NextCursor : null;
        }
        while (cursor is not null);

        collected.Should().HaveCount(expectedCount);
        collected.Distinct().Should().HaveCount(expectedCount, "курсор не должен возвращать дубликаты между страницами");
        collected.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetWalletBalance_DateBeforeAccountingStartDate_ReturnsZero()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-5);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);

        var response = await Client.GetAsync($"/api/v1/wallets/{wallet.Id}/balance?date={accountingStartDate.AddDays(-1):yyyy-MM-dd}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<BalanceSnapshotResponse>();
        decimal.Parse(body!.Balance.Amount, CultureInfo.InvariantCulture).Should().Be(0m, "Q13: баланс раньше AccountingStartDate равен нулю и не материализуется");
    }

    [Fact]
    public async Task GetWalletBalance_UnknownWallet_ReturnsNotFound()
    {
        await AuthenticateAsync();

        var response = await Client.GetAsync($"/api/v1/wallets/{Guid.NewGuid()}/balance");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ---- Stated balances (ADR-0003, §6): the snapshot of the stated date is the stated balance ----

    [Fact]
    public async Task StatedBalance_SnapshotOfTheStatedDateStaysEqualToItAfterEveryEditBeforeIt()
    {
        await AuthenticateAsync();
        var start = Today.AddDays(-20);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: start);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var statedDate = Today.AddDays(-8);
        var firstId = await CreateOperationAsync(wallet.Id, incomeTypeId, 500m, Today.AddDays(-15));
        await Client.SetStatedBalanceAsync(wallet.Id, statedDate, 350m);
        decimal SnapshotOn(IReadOnlyList<BalanceSnapshotResponse> snapshots, DateOnly date) => Amount(snapshots.Single(s => s.Date == date));

        // A retroactive operation before the stated date, an edit and a deletion: the stated date keeps 350 each time.
        var secondId = await CreateOperationAsync(wallet.Id, incomeTypeId, 70m, Today.AddDays(-12));
        var snapshots = await Client.GetSnapshotsAsync(wallet.Id, start, Today);
        SnapshotOn(snapshots, statedDate).Should().Be(350m);
        SnapshotOn(snapshots, Today.AddDays(-12)).Should().Be(570m, "the days before the stated one do include the new operation");
        SnapshotOn(snapshots, Today).Should().Be(350m);

        await Client.PatchAsJsonAsync($"/api/v1/operations/{firstId}", new
        {
            WalletId = wallet.Id, OperationTypeId = incomeTypeId, Amount = 800m, OperationDate = Today.AddDays(-15),
        });
        snapshots = await Client.GetSnapshotsAsync(wallet.Id, start, Today);
        SnapshotOn(snapshots, Today.AddDays(-15)).Should().Be(800m);
        SnapshotOn(snapshots, statedDate).Should().Be(350m);

        (await Client.DeleteAsync($"/api/v1/operations/{secondId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        snapshots = await Client.GetSnapshotsAsync(wallet.Id, start, Today);
        SnapshotOn(snapshots, Today.AddDays(-12)).Should().Be(800m);
        SnapshotOn(snapshots, statedDate).Should().Be(350m);
        SnapshotOn(snapshots, Today).Should().Be(350m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, statedDate)).Should().Be(350m);
    }

    [Fact]
    public async Task StatedBalance_ScheduledSnapshotPass_ChangesNothing()
    {
        await AuthenticateAsync();
        var start = Today.AddDays(-12);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: start);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await CreateOperationAsync(wallet.Id, incomeTypeId, 100m, Today.AddDays(-10));
        await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 250m);
        await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-2), 240m);
        var before = (await Client.GetSnapshotsAsync(wallet.Id, start, Today)).ToDictionary(s => s.Date, Amount);
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await connection.CountAsync("SELECT count(*) FROM audit.audit_entries");

        await Fixture.BackfillSnapshotsAsync(wallet.Id);

        var after = (await Client.GetSnapshotsAsync(wallet.Id, start, Today)).ToDictionary(s => s.Date, Amount);
        after.Should().BeEquivalentTo(before);
        after[Today.AddDays(-5)].Should().Be(250m);
        after[Today.AddDays(-2)].Should().Be(240m);
        after[Today].Should().Be(240m);
        (await connection.CountAsync("SELECT count(*) FROM audit.audit_entries")).Should().Be(auditBefore, "an unchanged pass writes no audit entries");
    }
}
