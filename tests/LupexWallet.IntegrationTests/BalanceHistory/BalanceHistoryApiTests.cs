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
/// слепков при создании/изменении/удалении операции задним числом (ADR-0003/ADR-0008).
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

    [Fact]
    public async Task DeleteOperation_CascadesRecalculationFromDeletedOperationDate()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-10);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: accountingStartDate);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        // ADR-0009, случай b (решение пользователя, 2026-09-11): удалить можно только операцию,
        // датированную сегодняшним днем — операция с более ранней датой уже считается отраженной
        // в истории и удалению не подлежит (см. DeleteOperation_DatedBeforeToday_ReturnsConflict).
        var operationId = await CreateOperationAsync(wallet.Id, incomeTypeId, 400m, Today);

        var deleteResponse = await Client.DeleteAsync($"/api/v1/operations/{operationId}");
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var page = await GetHistoryAsync(wallet.Id, accountingStartDate, Today, limit: 100);
        page.Data.Should().OnlyContain(s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture) == 1000m, "удаление операции откатывает её вклад в историю");
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
}
