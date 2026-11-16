using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Debts;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Reporting;

/// <summary>
/// ADR-0010 §4 (B9) and §7 through the HTTP API: a linked repayment releases the reserve and the
/// total amount (= free funds) changes once; deleting/restoring the repayment undoes/redoes the
/// release; free funds per wallet with deficit; upcoming payments; debts summary; cash-flow forecast.
/// </summary>
public sealed class PlanningReportsApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record TotalAmount(
        DateOnly? Date, MoneyResponse Amount, MoneyResponse ActualAmount, MoneyResponse? ReservedAmount, MoneyResponse? FreeAmount);
    private sealed record Fund(Guid Id, MoneyResponse Reserved);
    private sealed record WalletFreeFunds(
        Guid WalletId, Guid CurrencyId, bool IncludeInTotal, bool IsPrimary,
        MoneyResponse Balance, MoneyResponse Reserved, MoneyResponse Free, MoneyResponse Deficit);
    private sealed record FreeFunds(
        MoneyResponse ActualAmount, MoneyResponse ReservedAmount, MoneyResponse FreeAmount, MoneyResponse DeficitAmount,
        DateOnly RatesAsOfDate, string RateSource, List<WalletFreeFunds> Wallets);
    private sealed record UpcomingPayment(string Kind, DateOnly Date, bool IsOverdue, MoneyResponse Amount, MoneyResponse? ConvertedAmount, Guid? LoanId);
    private sealed record UpcomingPayments(DateOnly From, DateOnly To, MoneyResponse TotalAmount, MoneyResponse OverdueAmount, List<UpcomingPayment> Items);
    private sealed record AppliedRate(Guid FromCurrencyId, Guid ToCurrencyId, string Rate, DateOnly RateDate, string Source);
    private sealed record CreditorDebt(Guid CreditorId, string CreditorName, MoneyResponse Amount, int DebtCount);
    private sealed record DebtsSummary(
        MoneyResponse TotalDebtAmount, int DebtCount, List<CreditorDebt> Creditors, MoneyResponse TotalLoanRemainingAmount, int LoanCount,
        string RateSource, List<AppliedRate> Rates);
    private sealed record Movement(string Kind, DateOnly Date, DateOnly OriginalDate, bool IsOverdue, MoneyResponse ConvertedAmount);
    private sealed record ForecastDay(DateOnly Date, MoneyResponse ExpectedBalance, bool IsNegative, List<Movement> Movements);
    private sealed record NegativePeriod(DateOnly From, DateOnly To);
    private sealed record Forecast(
        DateOnly From, DateOnly To, int HorizonMonths, MoneyResponse StartBalance, MoneyResponse EndBalance, MoneyResponse MinBalance,
        DateOnly? FirstNegativeDate, List<NegativePeriod> NegativePeriods, List<ForecastDay> Days, List<string> Assumptions, string RateSource);
    private sealed record AuditChange(string Field, string? OldValue, string? NewValue);
    private sealed record AuditEntry(string EntityType, Guid EntityId, string Action, List<AuditChange> Changes);
    private sealed record AuditPage(List<AuditEntry> Data);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static decimal D(MoneyResponse money) => decimal.Parse(money.Amount, CultureInfo.InvariantCulture);

    private async Task<T> GetOkAsync<T>(string url)
    {
        var response = await Client.GetAsync(url);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private async Task<Fund> CreateFundAsync(Guid currencyId, Guid? debtId = null, Guid? loanId = null, string name = "Фонд")
    {
        var response = await Client.PostAsJsonAsync("/api/v1/funds", new
        {
            Name = name, TargetAmount = "5000", CurrencyId = currencyId, LinkedDebtId = debtId, LinkedLoanId = loanId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Fund>())!;
    }

    private async Task ReserveAsync(Guid fundId, Guid walletId, decimal amount)
    {
        var response = await Client.PostAsJsonAsync($"/api/v1/funds/{fundId}/reserve", new { WalletId = walletId, Amount = amount.ToString(CultureInfo.InvariantCulture) });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<decimal> FundReservedAsync(Guid fundId) => D((await GetOkAsync<Fund>($"/api/v1/funds/{fundId}")).Reserved);

    private async Task SpendAsync(Guid walletId, decimal amount)
    {
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", $"Расход-{Guid.NewGuid():N}");
        var response = await Client.PostAsJsonAsync("/api/v1/operations", new { WalletId = walletId, OperationTypeId = typeId, Amount = amount, OperationDate = Today });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
    }

    private async Task<Guid> PlanMonthlyExpenseAsync(Guid currencyId, decimal amount, DateOnly start)
    {
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", $"План-{Guid.NewGuid():N}");
        var response = await Client.PostAsJsonAsync("/api/v1/planned-transactions", new
        {
            OperationTypeId = typeId,
            Amount = amount.ToString(CultureInfo.InvariantCulture),
            CurrencyId = currencyId,
            Description = "Аренда",
            StartDate = start,
            Recurrence = new { Frequency = "Monthly", Interval = 1 },
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>()).GetProperty("id").GetGuid();
    }

    // ---- B9: linked repayment releases the reserve, deletion restores it ----

    [Fact]
    public async Task Repayment_ReleasesLinkedReserve_TotalChangesOnce_DeleteRestoresAndRestoreReleasesAgain()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: eur.Id, name: "Основной");
        var creditor = await Client.CreateCreditorAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, eur.Id, 300m);
        var fund = await CreateFundAsync(eur.Id, debtId: debt.Id, name: "На долг");
        await ReserveAsync(fund.Id, wallet.Id, 300m);

        var before = await GetOkAsync<TotalAmount>("/api/v1/reporting/total-amount");
        D(before.ActualAmount).Should().Be(1000m);
        D(before.ReservedAmount!).Should().Be(300m);
        D(before.Amount).Should().Be(700m, "B9: reserves reduce the total amount");
        D(before.FreeAmount!).Should().Be(700m);

        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Погашение");
        var repaid = await (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 200m)).ReadAsync<DebtResponse>();
        var operationId = repaid.Repayments.Single().OperationId;

        (await FundReservedAsync(fund.Id)).Should().Be(100m);
        var afterPayment = await GetOkAsync<TotalAmount>("/api/v1/reporting/total-amount");
        D(afterPayment.ActualAmount).Should().Be(800m);
        D(afterPayment.ReservedAmount!).Should().Be(100m);
        D(afterPayment.Amount).Should().Be(700m, "the money was already set aside: paying from the reserve does not reduce the total a second time");

        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await FundReservedAsync(fund.Id)).Should().Be(300m, "deleting the repayment brings the released reserve back");
        D((await GetOkAsync<TotalAmount>("/api/v1/reporting/total-amount")).Amount).Should().Be(700m);

        (await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await FundReservedAsync(fund.Id)).Should().Be(100m, "a restored repayment releases the reserve again");
        D((await GetOkAsync<TotalAmount>("/api/v1/reporting/total-amount")).Amount).Should().Be(700m);

        var audit = await GetOkAsync<AuditPage>($"/api/v1/audit-entries?entityType=Fund&entityId={fund.Id}");
        audit.Data.SelectMany(e => e.Changes).Should()
            .Contain(c => c.Field == "ReleaseReason" && c.NewValue == "LinkedPayment")
            .And.Contain(c => c.Field == "ReserveReason" && c.NewValue == "LinkedPaymentDeleted")
            .And.Contain(c => c.Field == "OperationId" && c.NewValue == operationId.ToString());
    }

    [Fact]
    public async Task DeletingLoanPayment_RestoresReserveOnlyWithinTheWalletFreeFunds()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var wallet = await Client.CreateWalletAsync(1000m, currencyId: eur.Id, name: "Основной");
        var creditor = await Client.CreateCreditorAsync();
        var loan = await Client.CreateLoanAsync(creditor.Id, eur.Id, 600m);
        var linked = await CreateFundAsync(eur.Id, loanId: loan.Id, name: "На кредит");
        await ReserveAsync(linked.Id, wallet.Id, 300m);

        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Платёж");
        var paid = await (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 300m)).ReadAsync<LoanResponse>();
        (await FundReservedAsync(linked.Id)).Should().Be(0m);

        // Balance 700: another fund takes all of it, then a spend leaves 500 (deficit 200).
        var other = await CreateFundAsync(eur.Id, name: "Отпуск");
        await ReserveAsync(other.Id, wallet.Id, 700m);
        await SpendAsync(wallet.Id, 200m);

        (await Client.DeleteAsync($"/api/v1/operations/{paid.Payments.Single().OperationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Balance 800 − reserved 700 = free 100: only that much of the 300 comes back.
        (await FundReservedAsync(linked.Id)).Should().Be(100m);
        (await FundReservedAsync(other.Id)).Should().Be(700m);
    }

    [Fact]
    public async Task FundLink_IsValidatedAgainstDebts()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        var creditor = await Client.CreateCreditorAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, eur.Id, 100m);

        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "x", TargetAmount = "10", CurrencyId = eur.Id, LinkedDebtId = Guid.NewGuid() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "x", TargetAmount = "10", CurrencyId = usd.Id, LinkedDebtId = debt.Id }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await Client.PostAsync($"/api/v1/debts/{debt.Id}/cancel", null)).EnsureSuccessStatusCode();
        (await Client.PostAsJsonAsync("/api/v1/funds", new { Name = "x", TargetAmount = "10", CurrencyId = eur.Id, LinkedDebtId = debt.Id }))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ---- #12: free funds per wallet, deficit ----

    [Fact]
    public async Task FreeFunds_PerWalletWithDeficit_AndSummary()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var main = await Client.CreateWalletAsync(500m, currencyId: eur.Id, name: "Основной");
        var second = await Client.CreateWalletAsync(100m, currencyId: eur.Id, name: "Второй");
        var fund = await CreateFundAsync(eur.Id);
        await ReserveAsync(fund.Id, main.Id, 400m);
        await SpendAsync(main.Id, 300m);

        var report = await GetOkAsync<FreeFunds>("/api/v1/reporting/free-funds");

        var mainRow = report.Wallets.Single(w => w.WalletId == main.Id);
        mainRow.IsPrimary.Should().BeTrue();
        D(mainRow.Balance).Should().Be(200m);
        D(mainRow.Reserved).Should().Be(400m);
        D(mainRow.Free).Should().Be(-200m);
        D(mainRow.Deficit).Should().Be(200m);
        D(report.Wallets.Single(w => w.WalletId == second.Id).Free).Should().Be(100m);
        D(report.ActualAmount).Should().Be(300m);
        D(report.ReservedAmount).Should().Be(400m);
        D(report.FreeAmount).Should().Be(-100m);
        D(report.DeficitAmount).Should().Be(200m);
        report.RateSource.Should().Be("Frankfurter");
    }

    // ---- #12: upcoming payments and the debts summary ----

    [Fact]
    public async Task UpcomingPayments_PlannedExpensesAndLoanSchedule_OverdueIncluded_NotSubtractedFromFreeFunds()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        await Client.CreateWalletAsync(1000m, currencyId: eur.Id, name: "Основной");
        await PlanMonthlyExpenseAsync(eur.Id, 400m, Today.AddDays(1));
        var creditor = await Client.CreateCreditorAsync();
        var loan = await Client.CreateLoanAsync(creditor.Id, eur.Id, 300m);
        (await Client.PutAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule", new
        {
            Items = new[]
            {
                new { DueDate = Today.AddDays(-5), Amount = 100m },
                new { DueDate = Today.AddDays(10), Amount = 100m },
                new { DueDate = Today.AddDays(40), Amount = 100m },
            },
        })).EnsureSuccessStatusCode();

        var report = await GetOkAsync<UpcomingPayments>("/api/v1/reporting/upcoming-payments");

        report.To.Should().Be(Today.AddDays(30));
        report.Items.Select(i => (i.Kind, i.Date)).Should().Equal(
            ("LoanPayment", Today.AddDays(-5)), ("PlannedExpense", Today.AddDays(1)), ("LoanPayment", Today.AddDays(10)));
        report.Items[0].IsOverdue.Should().BeTrue();
        D(report.TotalAmount).Should().Be(600m);
        D(report.OverdueAmount).Should().Be(100m);
        D((await GetOkAsync<TotalAmount>("/api/v1/reporting/total-amount")).Amount).Should().Be(1000m, "upcoming payments are not subtracted");

        (await GetOkAsync<UpcomingPayments>("/api/v1/reporting/upcoming-payments?days=60")).Items.Should().HaveCount(5);
        (await Client.GetAsync("/api/v1/reporting/upcoming-payments?days=0")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UpcomingPaymentsAndForecast_ScheduleAboveAmountToRepay_SubtractOnlyTheLoanRemaining()
    {
        // Review v0.4.0, R5: amount to repay 1000, schedule 4 × 300 = 1200, paid 900 — only 100 is still owed.
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        var wallet = await Client.CreateWalletAsync(2000m, currencyId: eur.Id, name: "Основной");
        var creditor = await Client.CreateCreditorAsync();
        var loan = await Client.CreateLoanAsync(creditor.Id, eur.Id, 1000m);
        (await Client.PutAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule", new
        {
            Items = new[]
            {
                new { DueDate = Today.AddDays(-20), Amount = 300m },
                new { DueDate = Today.AddDays(-10), Amount = 300m },
                new { DueDate = Today.AddDays(-1), Amount = 300m },
                new { DueDate = Today.AddDays(10), Amount = 300m },
            },
        })).EnsureSuccessStatusCode();
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Платёж");
        (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 900m)).EnsureSuccessStatusCode();

        var upcoming = await GetOkAsync<UpcomingPayments>("/api/v1/reporting/upcoming-payments");

        upcoming.Items.Should().ContainSingle(i => i.Kind == "LoanPayment").Which.Date.Should().Be(Today.AddDays(10));
        D(upcoming.Items.Single().Amount).Should().Be(100m);
        D(upcoming.TotalAmount).Should().Be(100m);

        var forecast = await GetOkAsync<Forecast>("/api/v1/reporting/cash-flow-forecast");
        forecast.Days.SelectMany(d => d.Movements).Where(m => m.Kind == "LoanPayment")
            .Should().ContainSingle().Which.ConvertedAmount.Should().Match<MoneyResponse>(m => D(m) == -100m);
        D(forecast.EndBalance).Should().Be(1100m - 100m);
    }

    [Fact]
    public async Task DebtsSummary_ConvertsToPrimaryCurrencyByLatestRate()
    {
        await AuthenticateAsync();
        var fake = new FrankfurterFake(Fixture.Factory.GetFrankfurterTestOverride()) { DefaultRate = 0.5m };
        var eur = await Client.CreateCurrencyAsync("EUR");
        var usd = await Client.CreateCurrencyAsync("USD");
        await Client.CreateWalletAsync(0m, currencyId: eur.Id, name: "Основной");
        await Client.CreateWalletAsync(0m, currencyId: usd.Id, name: "USD");
        (await Client.PostAsync("/api/v1/exchange-rates/refresh", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        var bank = await Client.CreateCreditorAsync("Банк");
        await Client.CreateDebtAsync(bank.Id, eur.Id, 100m);
        await Client.CreateDebtAsync(bank.Id, usd.Id, 50m);
        await Client.CreateLoanAsync(bank.Id, eur.Id, 1000m);

        var report = await GetOkAsync<DebtsSummary>("/api/v1/reporting/debts-summary");

        D(report.TotalDebtAmount).Should().Be(125m);
        report.TotalDebtAmount.CurrencyId.Should().Be(eur.Id);
        report.Creditors.Should().ContainSingle(c => c.CreditorId == bank.Id && c.DebtCount == 2);
        D(report.TotalLoanRemainingAmount).Should().Be(1000m);
        report.Rates.Should().ContainSingle(r => r.FromCurrencyId == usd.Id && r.Source == "Frankfurter" && r.RateDate == Today);
        _ = fake;
    }

    // ---- #10: cash-flow forecast ----

    [Fact]
    public async Task Forecast_MonthlyPlannedExpenseAndLoanSchedule_ExpectedBalancesAndNegativePeriod()
    {
        await AuthenticateAsync();
        var eur = await Client.CreateCurrencyAsync("EUR");
        await Client.CreateWalletAsync(1000m, currencyId: eur.Id, name: "Основной");
        var start = Today.AddDays(1);
        await PlanMonthlyExpenseAsync(eur.Id, 400m, start);
        var creditor = await Client.CreateCreditorAsync();
        var loan = await Client.CreateLoanAsync(creditor.Id, eur.Id, 900m);
        var firstLoanPayment = Today.AddDays(15);
        (await Client.PostAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule/generate", new { FirstPaymentDate = firstLoanPayment, NumberOfPayments = 3 }))
            .EnsureSuccessStatusCode();

        var forecast = await GetOkAsync<Forecast>("/api/v1/reporting/cash-flow-forecast");

        forecast.HorizonMonths.Should().Be(3);
        forecast.To.Should().Be(Today.AddMonths(3));
        D(forecast.StartBalance).Should().Be(1000m);
        var planned = forecast.Days.SelectMany(d => d.Movements).Where(m => m.Kind == "PlannedExpense").ToList();
        var loanPayments = forecast.Days.SelectMany(d => d.Movements).Where(m => m.Kind == "LoanPayment").ToList();
        planned.Should().OnlyContain(m => D(m.ConvertedAmount) == -400m);
        planned.Count.Should().Be(start.AddMonths(3) <= forecast.To ? 4 : 3);
        loanPayments.Select(m => m.Date).Should().Equal(firstLoanPayment, firstLoanPayment.AddMonths(1), firstLoanPayment.AddMonths(2));

        D(forecast.Days.Single(d => d.Date == start).ExpectedBalance).Should().Be(600m);
        D(forecast.Days.Single(d => d.Date == firstLoanPayment).ExpectedBalance).Should().Be(300m);
        forecast.FirstNegativeDate.Should().Be(start.AddMonths(1));
        forecast.Days.Single(d => d.Date == start.AddMonths(1)).IsNegative.Should().BeTrue();
        forecast.NegativePeriods.Should().ContainSingle(p => p.From == start.AddMonths(1) && p.To == forecast.To);
        D(forecast.EndBalance).Should().Be(1000m - planned.Count * 400m - 900m);
        D(forecast.MinBalance).Should().Be(D(forecast.EndBalance));
        forecast.Assumptions.Should().NotBeEmpty();

        (await GetOkAsync<Forecast>("/api/v1/reporting/cash-flow-forecast?months=12")).To.Should().Be(Today.AddMonths(12));
        (await Client.GetAsync("/api/v1/reporting/cash-flow-forecast?months=2")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Reports_WithoutPrimaryWallet_Return404()
    {
        await AuthenticateAsync();

        foreach (var url in new[] { "free-funds", "upcoming-payments", "debts-summary", "cash-flow-forecast" })
        {
            (await Client.GetAsync($"/api/v1/reporting/{url}")).StatusCode.Should().Be(HttpStatusCode.NotFound, url);
        }
    }
}
