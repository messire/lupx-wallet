using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Audit;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Planning.Api;
using LupexWallet.Planning.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.Planning;

/// <summary>
/// Planning API (docs/api/openapi.yaml, тег Planning; ADR-0010 §6): CRUD плана, вычисляемые наступления,
/// отклонения, исполнение через реальную операцию, связь с удалением операции, справочниками, кошельками и аудитом.
/// </summary>
public sealed class PlanningApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private sealed record Setup(WalletResponse Wallet, Guid CurrencyId, Guid ExpenseTypeId, Guid IncomeTypeId);

    private async Task<Setup> ArrangeAsync(decimal initialBalance = 1000m)
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalance, accountingStartDate: Today.AddYears(-1));
        var expense = await Client.CreateOperationTypeIdAsync("Expense", "Аренда");
        var income = await Client.CreateOperationTypeIdAsync("Income", "Зарплата");
        return new Setup(wallet, wallet.CurrencyId, expense, income);
    }

    private async Task<HttpResponseMessage> PostPlanAsync(
        Guid operationTypeId, Guid currencyId, DateOnly startDate, object? recurrence = null, Guid? walletId = null,
        string amount = "100", string? description = null) =>
        await Client.PostAsJsonAsync("/api/v1/planned-transactions", new
        {
            OperationTypeId = operationTypeId,
            Amount = amount,
            CurrencyId = currencyId,
            WalletId = walletId,
            Description = description,
            StartDate = startDate,
            Recurrence = recurrence,
        });

    private async Task<PlannedTransactionResponse> CreatePlanAsync(
        Guid operationTypeId, Guid currencyId, DateOnly startDate, object? recurrence = null, Guid? walletId = null, string amount = "100")
    {
        var response = await PostPlanAsync(operationTypeId, currencyId, startDate, recurrence, walletId, amount);
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlannedTransactionResponse>())!;
    }

    private async Task<PlannedOccurrenceListResponse> GetOccurrencesAsync(DateOnly from, DateOnly to, Guid? planId = null)
    {
        var url = $"/api/v1/planned-occurrences?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}" +
            (planId is { } id ? $"&plannedTransactionId={id}" : string.Empty);
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<PlannedOccurrenceListResponse>())!;
    }

    private string OccurrenceUrl(Guid planId, DateOnly date, string action) =>
        $"/api/v1/planned-transactions/{planId}/occurrences/{date:yyyy-MM-dd}/{action}";

    private async Task<HttpResponseMessage> ExecuteAsync(Guid planId, DateOnly date, object? body = null) =>
        body is null
            ? await Client.PostAsync(OccurrenceUrl(planId, date, "execute"), null)
            : await Client.PostAsJsonAsync(OccurrenceUrl(planId, date, "execute"), body);

    private async Task<WalletResponse> GetWalletAsync(Guid walletId) =>
        (await Client.GetFromJsonAsync<WalletResponse>($"/api/v1/wallets/{walletId}"))!;

    private static object Monthly(int? count = null) => new { Frequency = "Monthly", Interval = 1, Count = count };

    [Fact]
    public async Task Crud_CreateGetListUpdateFinish()
    {
        var setup = await ArrangeAsync();

        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, Monthly(), setup.Wallet.Id);
        plan.Direction.Should().Be("Expense");
        plan.Status.Should().Be("Active");
        plan.Amount.Amount.Should().Be("100");
        plan.Recurrence.Should().Be(new RecurrenceContract("Monthly", 1, null, null));

        var oneOff = await CreatePlanAsync(setup.IncomeTypeId, setup.CurrencyId, Today.AddDays(5), amount: "2500.123456789");
        oneOff.Recurrence.Should().BeNull();
        oneOff.Amount.Amount.Should().Be("2500.123456789", "ADR-0005: arbitrary precision");

        (await Client.GetFromJsonAsync<PlannedTransactionResponse>($"/api/v1/planned-transactions/{plan.Id}"))!.Id.Should().Be(plan.Id);
        (await Client.GetAsync($"/api/v1/planned-transactions/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var firstPage = (await Client.GetFromJsonAsync<PlannedTransactionPageResponse>("/api/v1/planned-transactions?limit=1"))!;
        firstPage.Data.Should().ContainSingle().Which.Id.Should().Be(plan.Id);
        firstPage.Pagination.HasMore.Should().BeTrue();
        var secondPage = (await Client.GetFromJsonAsync<PlannedTransactionPageResponse>(
            $"/api/v1/planned-transactions?limit=1&cursor={Uri.EscapeDataString(firstPage.Pagination.NextCursor!)}"))!;
        secondPage.Data.Should().ContainSingle().Which.Id.Should().Be(oneOff.Id);
        secondPage.Pagination.HasMore.Should().BeFalse();

        var update = await Client.PatchAsJsonAsync($"/api/v1/planned-transactions/{plan.Id}", new
        {
            OperationTypeId = setup.ExpenseTypeId,
            Amount = "120",
            CurrencyId = setup.CurrencyId,
            WalletId = setup.Wallet.Id,
            Description = "Аренда квартиры",
            StartDate = Today,
            Recurrence = new { Frequency = "weekly", Interval = 2, Until = Today.AddMonths(3) },
        });
        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
        var updated = (await update.Content.ReadFromJsonAsync<PlannedTransactionResponse>())!;
        updated.Recurrence.Should().Be(new RecurrenceContract("Weekly", 2, Today.AddMonths(3), null));
        updated.Description.Should().Be("Аренда квартиры");

        var finish = await Client.PostAsync($"/api/v1/planned-transactions/{plan.Id}/finish", null);
        finish.StatusCode.Should().Be(HttpStatusCode.OK);
        (await finish.Content.ReadFromJsonAsync<PlannedTransactionResponse>())!.Status.Should().Be("Finished");

        var active = (await Client.GetFromJsonAsync<PlannedTransactionPageResponse>("/api/v1/planned-transactions?status=Active"))!;
        active.Data.Select(p => p.Id).Should().Equal(oneOff.Id);

        (await Client.PostAsync($"/api/v1/planned-transactions/{plan.Id}/finish", null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Create_Validation()
    {
        var setup = await ArrangeAsync();
        var transferType = await Client.CreateOperationTypeIdAsync("Transfer", "Перевод-тип");
        var otherWallet = await Client.CreateWalletAsync(0m, name: "Другая валюта");

        (await PostPlanAsync(transferType, setup.CurrencyId, Today)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostPlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, amount: "0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostPlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, walletId: otherWallet.Id)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostPlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, new { Frequency = "Hourly" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostPlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, new { Frequency = "1" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostPlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, new { Frequency = "Daily", Until = Today, Count = 2 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PostPlanAsync(setup.ExpenseTypeId, Guid.NewGuid(), Today)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Occurrences_ComputedAcrossPlans_WithSkipMoveAndAmount()
    {
        var setup = await ArrangeAsync();
        var start = Today.AddDays(-40);
        var rent = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, start, Monthly(count: 4), setup.Wallet.Id);
        var bonus = await CreatePlanAsync(setup.IncomeTypeId, setup.CurrencyId, Today.AddDays(3), amount: "500");

        var all = await GetOccurrencesAsync(start, start.AddMonths(6));
        all.Data.Should().HaveCount(5);
        all.Data.Where(o => o.PlannedTransactionId == rent.Id).Select(o => o.OccurrenceDate)
            .Should().Equal(start, start.AddMonths(1), start.AddMonths(2), start.AddMonths(3));
        all.Data.First().IsOverdue.Should().BeTrue();
        all.Data.Single(o => o.PlannedTransactionId == bonus.Id).Direction.Should().Be("Income");

        var second = start.AddMonths(1);
        (await Client.PostAsync(OccurrenceUrl(rent.Id, start, "skip"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var moved = await Client.PostAsJsonAsync(OccurrenceUrl(rent.Id, second, "move"), new { Date = start.AddMonths(5) });
        moved.StatusCode.Should().Be(HttpStatusCode.OK);
        (await moved.Content.ReadFromJsonAsync<PlannedOccurrenceResponse>())!.EffectiveDate.Should().Be(start.AddMonths(5));
        var amount = await Client.PutAsJsonAsync(OccurrenceUrl(rent.Id, start.AddMonths(2), "amount"), new { Amount = "80.5" });
        amount.StatusCode.Should().Be(HttpStatusCode.OK);

        var onlyRent = await GetOccurrencesAsync(start, start.AddMonths(6), rent.Id);
        onlyRent.Data.Select(o => (o.OccurrenceDate, o.Status, o.ExpectedAmount.Amount)).Should().Equal(
            (start, "Skipped", "0"),
            (start.AddMonths(2), "Expected", "80.5"),
            (start.AddMonths(3), "Expected", "100"),
            (second, "Expected", "100"));

        // Moved out of a range that contains its original date.
        (await GetOccurrencesAsync(second, second, rent.Id)).Data.Should().BeEmpty();

        (await Client.PostAsync(OccurrenceUrl(rent.Id, start, "unskip"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.PostAsync(OccurrenceUrl(rent.Id, start, "unskip"), null)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.PostAsync(OccurrenceUrl(rent.Id, start.AddDays(1), "skip"), null)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await Client.GetAsync($"/api/v1/planned-occurrences?from={Today:yyyy-MM-dd}&to={Today.AddDays(-1):yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetAsync($"/api/v1/planned-occurrences?from={Today:yyyy-MM-dd}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Execute_CreatesOperation_PartialThenFull_SoftDeleteReturnsToExpected()
    {
        var setup = await ArrangeAsync(initialBalance: 1000m);
        var date = Today.AddDays(-3);
        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, date, walletId: setup.Wallet.Id, amount: "100");

        var partial = await ExecuteAsync(plan.Id, date, new { Amount = "30" });
        partial.StatusCode.Should().Be(HttpStatusCode.Created, await partial.Content.ReadAsStringAsync());
        var partialResult = (await partial.Content.ReadFromJsonAsync<PlannedOccurrenceExecutionResultResponse>())!;
        partialResult.Occurrence.Status.Should().Be("PartiallyExecuted");
        partialResult.Occurrence.ExpectedAmount.Amount.Should().Be("70");
        partial.Headers.Location!.ToString().Should().Be($"/api/v1/operations/{partialResult.OperationId}");
        (await GetWalletAsync(setup.Wallet.Id)).CurrentBalance.Amount.Should().Be("970");

        // Defaults: plan wallet, remaining amount, effective date.
        var rest = await ExecuteAsync(plan.Id, date);
        rest.StatusCode.Should().Be(HttpStatusCode.Created, await rest.Content.ReadAsStringAsync());
        var restResult = (await rest.Content.ReadFromJsonAsync<PlannedOccurrenceExecutionResultResponse>())!;
        restResult.Occurrence.Status.Should().Be("Executed");
        (await GetWalletAsync(setup.Wallet.Id)).CurrentBalance.Amount.Should().Be("900");

        var operation = await Client.GetFromJsonAsync<StatedBalanceCorrectionResponse>($"/api/v1/operations/{restResult.OperationId}");
        operation!.OperationDate.Should().Be(date);
        operation.OperationTypeId.Should().Be(setup.ExpenseTypeId);
        operation.Amount.Amount.Should().Be("70");

        (await ExecuteAsync(plan.Id, date)).StatusCode.Should().Be(HttpStatusCode.Conflict, "already fully executed");

        // Soft-deleting the execution operation puts its amount back into the expected one; restoring counts it again.
        (await Client.DeleteAsync($"/api/v1/operations/{restResult.OperationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDelete = (await GetOccurrencesAsync(date, date)).Data.Single();
        afterDelete.Status.Should().Be("PartiallyExecuted");
        afterDelete.ExpectedAmount.Amount.Should().Be("70");
        afterDelete.Executions.Should().Contain(e => e.OperationId == restResult.OperationId && e.IsOperationDeleted);

        (await Client.PostAsync($"/api/v1/operations/{restResult.OperationId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await GetOccurrencesAsync(date, date)).Data.Single().Status.Should().Be("Executed");

        // A plan with executions is finished instead of being deleted.
        var delete = await Client.DeleteAsync($"/api/v1/planned-transactions/{plan.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await delete.Content.ReadFromJsonAsync<PlannedTransactionResponse>())!.Status.Should().Be("Finished");
        (await GetOccurrencesAsync(date, date)).Data.Should().ContainSingle().Which.ExpectedAmount.Amount.Should().Be("0");
    }

    [Fact]
    public async Task Execute_Rejections_LeaveNoOperation()
    {
        var setup = await ArrangeAsync(initialBalance: 1000m);
        var otherWallet = await Client.CreateWalletAsync(0m, name: "Другая валюта");
        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today.AddDays(-1), Monthly());

        (await ExecuteAsync(plan.Id, Today.AddDays(-1))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "no wallet in plan or request");
        (await ExecuteAsync(plan.Id, Today.AddDays(-1), new { WalletId = otherWallet.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await ExecuteAsync(plan.Id, Today.AddDays(-1).AddMonths(1), new { WalletId = setup.Wallet.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the effective date is in the future");

        (await Client.PostAsync(OccurrenceUrl(plan.Id, Today.AddDays(-1), "skip"), null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ExecuteAsync(plan.Id, Today.AddDays(-1), new { WalletId = setup.Wallet.Id })).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await GetWalletAsync(setup.Wallet.Id)).CurrentBalance.Amount.Should().Be("1000");
        (await Client.GetFromJsonAsync<PlannedTransactionResponse>($"/api/v1/planned-transactions/{plan.Id}"))!.HasExecutions.Should().BeFalse();
    }

    [Fact]
    public async Task ConcurrentFullExecutions_OfOneOccurrence_OnlyOneSucceeds()
    {
        var setup = await ArrangeAsync(initialBalance: 1000m);
        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, walletId: setup.Wallet.Id, amount: "100");

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => ExecuteAsync(plan.Id, Today)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(3);
        (await GetWalletAsync(setup.Wallet.Id)).CurrentBalance.Amount.Should().Be("900");
    }

    [Fact]
    public async Task Delete_WithoutExecutions_IsPhysical_AndAudited()
    {
        var setup = await ArrangeAsync();
        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, Monthly());
        (await Client.PostAsync(OccurrenceUrl(plan.Id, Today, "skip"), null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await Client.DeleteAsync($"/api/v1/planned-transactions/{plan.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"/api/v1/planned-transactions/{plan.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var audit = (await Client.GetFromJsonAsync<AuditEntryPageResponse>(
            $"/api/v1/audit-entries?entityType=PlannedTransaction&entityId={plan.Id}"))!;
        audit.Data.Select(e => e.Action).Should().Equal("Deleted", "OccurrenceSkipped", "Created");
        audit.Data.Single(e => e.Action == "OccurrenceSkipped").Changes
            .Should().Contain(c => c.Field == "OccurrenceDate" && c.NewValue == Today.ToString("yyyy-MM-dd"));
        audit.Data.Single(e => e.Action == "Created").Changes.Should().Contain(c => c.Field == "Amount" && c.NewValue == "100");
    }

    [Fact]
    public async Task Execute_IsAudited_ForPlanAndOperation()
    {
        var setup = await ArrangeAsync();
        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, walletId: setup.Wallet.Id);

        var result = (await (await ExecuteAsync(plan.Id, Today)).Content.ReadFromJsonAsync<PlannedOccurrenceExecutionResultResponse>())!;

        var planAudit = (await Client.GetFromJsonAsync<AuditEntryPageResponse>(
            $"/api/v1/audit-entries?entityType=PlannedTransaction&entityId={plan.Id}"))!;
        planAudit.Data.First().Action.Should().Be("OccurrenceExecuted");
        planAudit.Data.First().Changes.Should().Contain(c => c.Field == "OperationId" && c.NewValue == result.OperationId.ToString());

        var operationAudit = (await Client.GetFromJsonAsync<AuditEntryPageResponse>(
            $"/api/v1/audit-entries?entityType=Operation&entityId={result.OperationId}"))!;
        operationAudit.Data.Should().ContainSingle(e => e.Action == "Created");
    }

    [Fact]
    public async Task OperationTypeAndCurrency_UsedByPlan_CannotBeDeleted()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("PLN", "Злотый");
        var type = await Client.CreateOperationTypeIdAsync("Expense", "Подписка");
        var plan = await CreatePlanAsync(type, currency.Id, Today.AddDays(10));

        (await Client.DeleteAsync($"/api/v1/operation-types/{type}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = Fixture.Factory.Services.CreateScope();
        var probe = scope.ServiceProvider.GetServices<IReferenceItemUsageProbe>().Single(p => p.GetType().Name == "PlanningReferenceItemUsageProbe");
        (await probe.IsUsedAsync(ReferenceItemKind.OperationType, type, CancellationToken.None)).Should().BeTrue();
        (await probe.IsUsedAsync(ReferenceItemKind.Currency, Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();
        (await probe.IsUsedAsync(ReferenceItemKind.WalletType, Guid.NewGuid(), CancellationToken.None)).Should().BeFalse();

        (await Client.DeleteAsync($"/api/v1/planned-transactions/{plan.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"/api/v1/operation-types/{type}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task DeletedWallet_IsUnlinkedFromPlans()
    {
        var setup = await ArrangeAsync();
        var spare = await Client.CreateWalletAsync(0m, name: "Запасной", currencyId: setup.CurrencyId);
        var plan = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today, walletId: spare.Id);

        (await Client.DeleteAsync($"/api/v1/wallets/{spare.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Client.GetFromJsonAsync<PlannedTransactionResponse>($"/api/v1/planned-transactions/{plan.Id}"))!.WalletId.Should().BeNull();
        var audit = (await Client.GetFromJsonAsync<AuditEntryPageResponse>(
            $"/api/v1/audit-entries?entityType=PlannedTransaction&entityId={plan.Id}"))!;
        audit.Data.First().Action.Should().Be("Updated");
        audit.Data.First().Changes.Should().Contain(c => c.Field == "WalletId" && c.NewValue == null);
    }

    [Fact]
    public async Task PlannedOccurrencesSource_ReturnsSignedRemaindersOfActivePlans()
    {
        var setup = await ArrangeAsync();
        var expense = await CreatePlanAsync(setup.ExpenseTypeId, setup.CurrencyId, Today.AddDays(-1), walletId: setup.Wallet.Id, amount: "100");
        await CreatePlanAsync(setup.IncomeTypeId, setup.CurrencyId, Today.AddDays(2), amount: "50");
        var finished = await CreatePlanAsync(setup.IncomeTypeId, setup.CurrencyId, Today.AddDays(3), amount: "70");
        (await Client.PostAsync($"/api/v1/planned-transactions/{finished.Id}/finish", null)).EnsureSuccessStatusCode();
        (await ExecuteAsync(expense.Id, Today.AddDays(-1), new { Amount = "40" })).EnsureSuccessStatusCode();

        using var scope = Fixture.Factory.Services.CreateScope();
        var source = scope.ServiceProvider.GetRequiredService<IPlannedOccurrencesSource>();
        var expected = await source.ListExpectedAsync(Today.AddDays(-30), Today.AddDays(30), CancellationToken.None);

        expected.Select(e => (e.EffectiveDate, e.SignedAmount, e.IsOverdue)).Should().Equal(
            (Today.AddDays(-1), -60m, true),
            (Today.AddDays(2), 50m, false));
        expected[0].WalletId.Should().Be(new WalletId(setup.Wallet.Id));
        expected.Should().OnlyContain(e => e.CurrencyId == new CurrencyId(setup.CurrencyId));
    }
}
