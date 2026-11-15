using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Audit;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Budgeting;

// DTOs of docs/api/openapi.yaml, tags ExpenseCategories and Budgets.
public sealed record ExpenseCategoryDtoResponse(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> OperationTypeIds);
public sealed record ExpenseCategoryPageDtoResponse(IReadOnlyList<ExpenseCategoryDtoResponse> Data, CursorPageMetaResponse Pagination);
public sealed record CategoryLimitDtoResponse(Guid ExpenseCategoryId, string Limit);
public sealed record BudgetDtoResponse(Guid Id, string Month, Guid CurrencyId, IReadOnlyList<CategoryLimitDtoResponse> Limits);
public sealed record BudgetPageDtoResponse(IReadOnlyList<BudgetDtoResponse> Data, CursorPageMetaResponse Pagination);
public sealed record CategoryExecutionDtoResponse(
    Guid ExpenseCategoryId, string Name, bool IsActive, string Limit, string Actual, string Remaining, bool IsOverspent);
public sealed record UnbudgetedDtoResponse(string Actual);
public sealed record BudgetTotalsDtoResponse(string Limit, string BudgetedActual, string Remaining, string Actual);
public sealed record AppliedRateDtoResponse(Guid FromCurrencyId, DateOnly OperationDate, DateOnly RateDate, string Rate, bool IsFallback);
public sealed record ExcludedOperationDtoResponse(Guid OperationId, Guid CurrencyId, DateOnly OperationDate, string Amount, string Reason);
public sealed record BudgetExecutionDtoResponse(
    Guid BudgetId, string Month, Guid CurrencyId, IReadOnlyList<CategoryExecutionDtoResponse> Categories,
    UnbudgetedDtoResponse Unbudgeted, BudgetTotalsDtoResponse Totals, string RateSource,
    IReadOnlyList<AppliedRateDtoResponse> AppliedRates, IReadOnlyList<ExcludedOperationDtoResponse> ExcludedOperations);

/// <summary>Budgeting API (ADR-0010 §5): expense categories, monthly budgets and their execution.</summary>
public sealed class BudgetingApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private readonly FrankfurterFake _fake = new(fixture.Factory.GetFrankfurterTestableHandler());

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    /// <summary>Last full month: all its days are in the past.</summary>
    private static readonly DateOnly MonthStart = new DateOnly(Today.Year, Today.Month, 1).AddMonths(-1);

    private static string MonthText => MonthStart.ToString("yyyy-MM", CultureInfo.InvariantCulture);

    private static decimal Dec(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    private async Task<HttpResponseMessage> CreateCategoryRawAsync(string name, params Guid[] typeIds) =>
        await Client.PostAsJsonAsync("/api/v1/expense-categories", new { Name = name, OperationTypeIds = typeIds });

    private async Task<ExpenseCategoryDtoResponse> CreateCategoryAsync(string name, params Guid[] typeIds)
    {
        var response = await CreateCategoryRawAsync(name, typeIds);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ExpenseCategoryDtoResponse>())!;
    }

    private async Task<BudgetDtoResponse> CreateBudgetAsync(Guid currencyId, string? month = null)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/budgets", new { Month = month ?? MonthText, CurrencyId = currencyId });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BudgetDtoResponse>())!;
    }

    private async Task<HttpResponseMessage> SetLimitRawAsync(Guid budgetId, Guid categoryId, string limit) =>
        await Client.PutAsJsonAsync($"/api/v1/budgets/{budgetId}/limits/{categoryId}", new { Limit = limit });

    private async Task SetLimitAsync(Guid budgetId, Guid categoryId, string limit)
    {
        var response = await SetLimitRawAsync(budgetId, categoryId, limit);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<BudgetExecutionDtoResponse> GetExecutionAsync(Guid budgetId)
    {
        var response = await Client.GetAsync($"/api/v1/budgets/{budgetId}/execution");
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<BudgetExecutionDtoResponse>())!;
    }

    private async Task<string> ProblemDetailAsync(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("detail").GetString()!;

    // ---- Expense categories ----

    [Fact]
    public async Task Category_CreateUpdateList_ValidatesMemberTypes()
    {
        await AuthenticateAsync();
        var food = await Client.CreateOperationTypeIdAsync("Expense", "Еда");
        var cafe = await Client.CreateOperationTypeIdAsync("Expense", "Кафе");
        var salary = await Client.CreateOperationTypeIdAsync("Income", "Зарплата");

        var category = await CreateCategoryAsync("  Питание ", food);
        category.Name.Should().Be("Питание");
        category.IsActive.Should().BeTrue();
        category.OperationTypeIds.Should().Equal(food);

        var notExpense = await CreateCategoryRawAsync("Доходы", salary);
        notExpense.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ProblemDetailAsync(notExpense)).Should().Contain("«Зарплата»").And.Contain("не является расходом");

        var unknown = await CreateCategoryRawAsync("Неизвестно", Guid.NewGuid());
        unknown.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var taken = await CreateCategoryRawAsync("Ещё еда", food);
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemDetailAsync(taken)).Should().Contain("«Еда»").And.Contain("«Питание»");

        var duplicateName = await CreateCategoryRawAsync("питание");
        duplicateName.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var patch = await Client.PatchAsJsonAsync($"/api/v1/expense-categories/{category.Id}",
            new { Name = "Еда и кафе", OperationTypeIds = new[] { food, cafe } });
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await patch.Content.ReadFromJsonAsync<ExpenseCategoryDtoResponse>())!;
        updated.Name.Should().Be("Еда и кафе");
        updated.OperationTypeIds.Should().BeEquivalentTo([food, cafe]);

        // Only the name: the member set stays.
        var rename = await Client.PatchAsJsonAsync($"/api/v1/expense-categories/{category.Id}", new { Name = "Еда" });
        (await rename.Content.ReadFromJsonAsync<ExpenseCategoryDtoResponse>())!.OperationTypeIds.Should().HaveCount(2);

        var list = (await Client.GetFromJsonAsync<ExpenseCategoryPageDtoResponse>("/api/v1/expense-categories"))!;
        list.Data.Should().ContainSingle(c => c.Id == category.Id && c.Name == "Еда");
        list.Pagination.HasMore.Should().BeFalse();

        (await Client.GetAsync($"/api/v1/expense-categories/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Category_DeleteUnused_DeleteUsedIsConflict_DeactivateKeepsItInBudget()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR");
        var free = await CreateCategoryAsync("Свободная");
        var used = await CreateCategoryAsync("Используемая");
        var budget = await CreateBudgetAsync(currency.Id);
        await SetLimitAsync(budget.Id, used.Id, "100");

        (await Client.DeleteAsync($"/api/v1/expense-categories/{free.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var conflict = await Client.DeleteAsync($"/api/v1/expense-categories/{used.Id}");
        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemDetailAsync(conflict)).Should().Contain("используйте деактивацию");

        var deactivate = await Client.PostAsync($"/api/v1/expense-categories/{used.Id}/deactivate", null);
        deactivate.StatusCode.Should().Be(HttpStatusCode.OK);
        (await deactivate.Content.ReadFromJsonAsync<ExpenseCategoryDtoResponse>())!.IsActive.Should().BeFalse();

        // A deactivated category is hidden from the default list, cannot get a new limit, but stays in the existing budget.
        var activeOnly = (await Client.GetFromJsonAsync<ExpenseCategoryPageDtoResponse>("/api/v1/expense-categories"))!;
        activeOnly.Data.Should().NotContain(c => c.Id == used.Id);
        var all = (await Client.GetFromJsonAsync<ExpenseCategoryPageDtoResponse>("/api/v1/expense-categories?includeInactive=true"))!;
        all.Data.Should().Contain(c => c.Id == used.Id);

        (await SetLimitRawAsync(budget.Id, used.Id, "200")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var execution = await GetExecutionAsync(budget.Id);
        execution.Categories.Should().ContainSingle(c => c.ExpenseCategoryId == used.Id && !c.IsActive && Dec(c.Limit) == 100m);
    }

    [Fact]
    public async Task OperationTypeInCategory_AndCurrencyOfBudget_CountAsUsedByReferenceData()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("EUR");
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", "Транспорт");
        var category = await CreateCategoryAsync("Транспорт", typeId);
        var budget = await CreateBudgetAsync(currency.Id);

        (await Client.DeleteAsync($"/api/v1/operation-types/{typeId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await Client.PatchAsJsonAsync($"/api/v1/expense-categories/{category.Id}", new { OperationTypeIds = Array.Empty<Guid>() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.DeleteAsync($"/api/v1/budgets/{budget.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.DeleteAsync($"/api/v1/operation-types/{typeId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ---- Budgets ----

    [Fact]
    public async Task Budget_CreateListLimitsDelete()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        var category = await CreateCategoryAsync("Дом");

        var budget = await CreateBudgetAsync(eur.Id);
        budget.Month.Should().Be(MonthText);
        budget.Limits.Should().BeEmpty();
        await CreateBudgetAsync(usd.Id);
        var nextMonth = MonthStart.AddMonths(1).ToString("yyyy-MM", CultureInfo.InvariantCulture);
        await CreateBudgetAsync(eur.Id, nextMonth);

        var duplicate = await Client.PostAsJsonAsync("/api/v1/budgets", new { Month = MonthText, CurrencyId = eur.Id });
        duplicate.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await ProblemDetailAsync(duplicate)).Should().Contain("EUR");
        (await Client.PostAsJsonAsync("/api/v1/budgets", new { Month = "2026-13", CurrencyId = eur.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PostAsJsonAsync("/api/v1/budgets", new { Month = MonthText, CurrencyId = Guid.NewGuid() })).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var byMonth = (await Client.GetFromJsonAsync<BudgetPageDtoResponse>($"/api/v1/budgets?month={MonthText}"))!;
        byMonth.Data.Should().HaveCount(2).And.OnlyContain(b => b.Month == MonthText);
        var all = (await Client.GetFromJsonAsync<BudgetPageDtoResponse>("/api/v1/budgets?limit=2"))!;
        all.Data.Select(b => b.Month).Should().Equal(nextMonth, MonthText);
        all.Pagination.HasMore.Should().BeTrue();
        var rest = (await Client.GetFromJsonAsync<BudgetPageDtoResponse>($"/api/v1/budgets?limit=2&cursor={Uri.EscapeDataString(all.Pagination.NextCursor!)}"))!;
        rest.Data.Should().ContainSingle().Which.Month.Should().Be(MonthText);
        (await Client.GetAsync("/api/v1/budgets?month=bad")).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await SetLimitRawAsync(budget.Id, category.Id, "-1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SetLimitRawAsync(budget.Id, Guid.NewGuid(), "1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await SetLimitRawAsync(Guid.NewGuid(), category.Id, "1")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        await SetLimitAsync(budget.Id, category.Id, "1500.123456789");
        var got = (await Client.GetFromJsonAsync<BudgetDtoResponse>($"/api/v1/budgets/{budget.Id}"))!;
        got.Limits.Should().ContainSingle(l => l.ExpenseCategoryId == category.Id && l.Limit == "1500.123456789");

        var removed = await Client.DeleteAsync($"/api/v1/budgets/{budget.Id}/limits/{category.Id}");
        removed.StatusCode.Should().Be(HttpStatusCode.OK);
        (await removed.Content.ReadFromJsonAsync<BudgetDtoResponse>())!.Limits.Should().BeEmpty();
        (await Client.DeleteAsync($"/api/v1/budgets/{budget.Id}/limits/{category.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Client.DeleteAsync($"/api/v1/budgets/{budget.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"/api/v1/budgets/{budget.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Client.GetAsync($"/api/v1/budgets/{budget.Id}/execution")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Budget_ChangesAreAudited_WithLimitsInTheBudgetRecord()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", "Связь");
        var category = await CreateCategoryAsync("Связь", typeId);
        var budget = await CreateBudgetAsync(eur.Id);
        await SetLimitAsync(budget.Id, category.Id, "10");
        await SetLimitAsync(budget.Id, category.Id, "12.5");
        (await Client.DeleteAsync($"/api/v1/budgets/{budget.Id}/limits/{category.Id}")).EnsureSuccessStatusCode();

        var budgetAudit = (await Client.GetFromJsonAsync<AuditEntryPageResponse>(
            $"/api/v1/audit-entries?entityType=Budget&entityId={budget.Id}"))!.Data;
        budgetAudit.Select(e => e.Action).Should().BeEquivalentTo(["Created", "Updated", "Updated", "Updated"]);
        var field = $"Limit[{category.Id}]";
        budgetAudit.SelectMany(e => e.Changes).Where(c => c.Field == field)
            .Select(c => (c.OldValue, c.NewValue))
            .Should().BeEquivalentTo(new (string?, string?)[] { (null, "10"), ("10", "12.5"), ("12.5", null) });

        var categoryAudit = (await Client.GetFromJsonAsync<AuditEntryPageResponse>(
            $"/api/v1/audit-entries?entityType=ExpenseCategory&entityId={category.Id}"))!.Data;
        categoryAudit.Should().ContainSingle(e => e.Action == "Created"
            && e.Changes.Any(c => c.Field == "OperationTypeIds" && c.NewValue == typeId.ToString())
            && e.Changes.Any(c => c.Field == "Name" && c.NewValue == "Связь"));
    }

    // ---- Execution ----

    [Fact]
    public async Task Execution_SameCurrency_SumsExpensesPerCategoryAcrossWallets_WithUnbudgetedRowAndOverspend()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var start = MonthStart.AddDays(-40);
        var walletA = await Client.CreateWalletAsync(1000m, start, "A", currencyId: eur.Id);
        var walletB = await Client.CreateWalletAsync(1000m, start, "B", currencyId: eur.Id);
        var food = await Client.CreateOperationTypeIdAsync("Expense", "Еда");
        var cafe = await Client.CreateOperationTypeIdAsync("Expense", "Кафе");
        var fuel = await Client.CreateOperationTypeIdAsync("Expense", "Бензин");
        var gifts = await Client.CreateOperationTypeIdAsync("Expense", "Подарки");
        var misc = await Client.CreateOperationTypeIdAsync("Expense", "Разное");
        var salary = await Client.CreateOperationTypeIdAsync("Income", "Зарплата");
        var eating = await CreateCategoryAsync("Питание", food, cafe);
        var car = await CreateCategoryAsync("Машина", fuel);
        await CreateCategoryAsync("Праздники", gifts); // category without a limit in this budget
        var budget = await CreateBudgetAsync(eur.Id);
        await SetLimitAsync(budget.Id, eating.Id, "100");
        await SetLimitAsync(budget.Id, car.Id, "50");

        await Client.CreateOperationReturningIdAsync(walletA.Id, food, 30m, MonthStart);
        await Client.CreateOperationReturningIdAsync(walletB.Id, cafe, 25.5m, MonthStart.AddMonths(1).AddDays(-1));
        await Client.CreateOperationReturningIdAsync(walletA.Id, fuel, 70m, MonthStart.AddDays(3));
        await Client.CreateOperationReturningIdAsync(walletB.Id, gifts, 15m, MonthStart.AddDays(4));
        await Client.CreateOperationReturningIdAsync(walletA.Id, misc, 5m, MonthStart.AddDays(5));
        await Client.CreateOperationReturningIdAsync(walletA.Id, salary, 999m, MonthStart.AddDays(5));
        await Client.CreateOperationReturningIdAsync(walletA.Id, food, 1000m, MonthStart.AddDays(-1)); // previous month
        var deleted = await Client.CreateOperationReturningIdAsync(walletA.Id, food, 500m, MonthStart.AddDays(6));
        (await Client.DeleteAsync($"/api/v1/operations/{deleted}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await Client.CreateOperationTypeIdAsync("Transfer");
        (await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = walletA.Id, TargetWalletId = walletB.Id, Amount = 77m, TransferDate = MonthStart.AddDays(7),
        })).EnsureSuccessStatusCode();

        var execution = await GetExecutionAsync(budget.Id);

        execution.Month.Should().Be(MonthText);
        execution.CurrencyId.Should().Be(eur.Id);
        var eatingRow = execution.Categories.Single(c => c.ExpenseCategoryId == eating.Id);
        (Dec(eatingRow.Actual), Dec(eatingRow.Remaining), eatingRow.IsOverspent).Should().Be((55.5m, 44.5m, false));
        var carRow = execution.Categories.Single(c => c.ExpenseCategoryId == car.Id);
        (Dec(carRow.Actual), Dec(carRow.Remaining), carRow.IsOverspent).Should().Be((70m, -20m, true));
        execution.Categories.Should().HaveCount(2, "only categories with a limit get a row");
        Dec(execution.Unbudgeted.Actual).Should().Be(20m, "gifts (category without a limit) and misc (no category)");
        Dec(execution.Totals.Limit).Should().Be(150m);
        Dec(execution.Totals.BudgetedActual).Should().Be(125.5m);
        Dec(execution.Totals.Remaining).Should().Be(24.5m);
        Dec(execution.Totals.Actual).Should().Be(145.5m);
        execution.AppliedRates.Should().BeEmpty();
        execution.ExcludedOperations.Should().BeEmpty();
        execution.RateSource.Should().Be("Frankfurter");
    }

    [Fact]
    public async Task Execution_OtherCurrency_UsesHistoricalRateOnOperationDate_FallsBackToEarlierAndInverse()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        var start = MonthStart.AddDays(-40);
        await Client.CreateWalletAsync(0m, start, "Основной EUR", currencyId: eur.Id);
        var usdWallet = await Client.CreateWalletAsync(1000m, start, "USD", currencyId: usd.Id);
        var eurWallet = await Client.CreateWalletAsync(1000m, start, "EUR 2", currencyId: eur.Id);
        var food = await Client.CreateOperationTypeIdAsync("Expense", "Еда");
        var category = await CreateCategoryAsync("Питание", food);

        // History USD -> EUR (primary): a distinct rate per day; weekends have no rate.
        static decimal RateOn(DateOnly d) => 0.5m + (d.DayNumber % 10) / 100m;
        _fake.RateForDate = (_, _, date) => RateOn(date);
        await Fixture.RunExchangeRatesBackgroundPassAsync();

        var weekday = MonthStart;
        while (weekday.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
        {
            weekday = weekday.AddDays(1);
        }

        var saturday = MonthStart.AddDays(7);
        while (saturday.DayOfWeek != DayOfWeek.Saturday)
        {
            saturday = saturday.AddDays(1);
        }

        var friday = saturday.AddDays(-1);
        await Client.CreateOperationReturningIdAsync(usdWallet.Id, food, 100m, weekday);
        await Client.CreateOperationReturningIdAsync(usdWallet.Id, food, 10m, saturday);
        await Client.CreateOperationReturningIdAsync(eurWallet.Id, food, 7m, weekday);

        var eurBudget = await CreateBudgetAsync(eur.Id);
        await SetLimitAsync(eurBudget.Id, category.Id, "1000");
        var execution = await GetExecutionAsync(eurBudget.Id);

        var expected = 100m * RateOn(weekday) + 10m * RateOn(friday) + 7m;
        Dec(execution.Categories.Single().Actual).Should().Be(expected);
        execution.ExcludedOperations.Should().BeEmpty();
        execution.AppliedRates.Select(r => (r.FromCurrencyId, r.OperationDate, r.RateDate, Dec(r.Rate), r.IsFallback))
            .Should().Equal(
                (usd.Id, weekday, weekday, RateOn(weekday), false),
                (usd.Id, saturday, friday, RateOn(friday), true));

        // A budget in the secondary currency converts the primary-currency expense through the inverse pair.
        var usdBudget = await CreateBudgetAsync(usd.Id);
        await SetLimitAsync(usdBudget.Id, category.Id, "1000");
        var usdExecution = await GetExecutionAsync(usdBudget.Id);
        var inverse = usdExecution.AppliedRates.Should().ContainSingle().Subject;
        inverse.FromCurrencyId.Should().Be(eur.Id);
        inverse.IsFallback.Should().BeFalse();
        Dec(usdExecution.Categories.Single().Actual).Should().BeApproximately(110m + 7m / RateOn(weekday), 0.0000000001m);
    }

    [Fact]
    public async Task Execution_WithoutAnyRate_ExcludesTheOperationWithReason()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        var start = MonthStart.AddDays(-40);
        await Client.CreateWalletAsync(0m, start, "Основной EUR", currencyId: eur.Id);
        var usdWallet = await Client.CreateWalletAsync(1000m, start, "USD", currencyId: usd.Id);
        var food = await Client.CreateOperationTypeIdAsync("Expense", "Еда");
        var category = await CreateCategoryAsync("Питание", food);
        var operationDate = MonthStart.AddDays(2);
        var operationId = await Client.CreateOperationReturningIdAsync(usdWallet.Id, food, 40m, operationDate);
        var budget = await CreateBudgetAsync(eur.Id);
        await SetLimitAsync(budget.Id, category.Id, "100");

        // No history was loaded (no background pass).
        var execution = await GetExecutionAsync(budget.Id);

        Dec(execution.Categories.Single().Actual).Should().Be(0m);
        var excluded = execution.ExcludedOperations.Should().ContainSingle().Subject;
        (excluded.OperationId, excluded.CurrencyId, excluded.OperationDate, Dec(excluded.Amount))
            .Should().Be((operationId, usd.Id, operationDate, 40m));
        excluded.Reason.Should().Be($"Курс валюты USD к EUR на дату {operationDate:yyyy-MM-dd} недоступен.");
        execution.AppliedRates.Should().BeEmpty();
    }
}
