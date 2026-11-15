using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Audit;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Debts;

/// <summary>
/// Loans end-to-end (ADR-0010 §3; issue #8): schedule generation and editing, payments as real
/// expenses matched against the earliest unpaid items, computed statuses, next payment, lifecycle.
/// </summary>
public sealed class LoansApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateOnly Today = DebtsTestHelpers.Today;

    private async Task<(WalletResponse Wallet, Guid ExpenseTypeId, LoanResponse Loan)> ArrangeAsync(decimal amount = 300m)
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 10_000m);
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Платёж по кредиту");
        var creditor = await Client.CreateCreditorAsync("Банк");
        var loan = await Client.CreateLoanAsync(creditor.Id, wallet.CurrencyId, amount);
        return (wallet, expenseTypeId, loan);
    }

    [Fact]
    public async Task GenerateSchedule_EqualMonthlyPayments_FromDate()
    {
        var (_, _, loan) = await ArrangeAsync(1000m);
        var first = Today.AddMonths(1);

        var generated = await (await Client.PostAsJsonAsync(
            $"/api/v1/loans/{loan.Id}/schedule/generate", new { FirstPaymentDate = first, NumberOfPayments = 3 })).ReadAsync<LoanResponse>();

        generated.Schedule.Select(x => x.DueDate).Should().Equal(first, first.AddMonths(1), first.AddMonths(2));
        generated.Schedule.Select(x => x.Amount.Value()).Should().Equal(333.33m, 333.33m, 333.34m);
        generated.Schedule.Should().OnlyContain(x => x.Status == "Expected");
        generated.ScheduledAmount.Value().Should().Be(1000m);
        generated.NextPayment!.DueDate.Should().Be(first);
        generated.RemainingAmount.Value().Should().Be(1000m);

        (await Client.PostAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule/generate", new { FirstPaymentDate = first, NumberOfPayments = 0 }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Payments_MatchEarliestUnpaidItems_StatusesOverdueAndPartial_NextPaymentAndRemaining()
    {
        var (wallet, expenseTypeId, loan) = await ArrangeAsync(300m);
        await (await Client.PutAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule", new
        {
            Items = new[]
            {
                new { DueDate = Today.AddMonths(-2), Amount = 100m },
                new { DueDate = Today.AddMonths(-1), Amount = 100m },
                new { DueDate = Today.AddMonths(1), Amount = 100m },
            },
        })).ReadAsync<LoanResponse>();

        var afterFirst = await (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 150m)).ReadAsync<LoanResponse>();

        afterFirst.Schedule.Select(x => x.Status).Should().Equal("Paid", "Overdue", "Expected");
        afterFirst.Schedule[1].PaidAmount.Value().Should().Be(50m);
        afterFirst.NextPayment!.DueDate.Should().Be(Today.AddMonths(-1));
        afterFirst.NextPayment.OutstandingAmount.Value().Should().Be(50m);
        afterFirst.RemainingAmount.Value().Should().Be(150m);
        afterFirst.Payments.Should().ContainSingle(p => p.IsCounted);

        var afterSecond = await (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 80m)).ReadAsync<LoanResponse>();
        afterSecond.Schedule.Select(x => x.Status).Should().Equal("Paid", "Paid", "Partial");
        afterSecond.NextPayment!.OutstandingAmount.Value().Should().Be(70m);

        (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 70.01m)).StatusCode
            .Should().Be(HttpStatusCode.Conflict);

        var repaid = await (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 70m)).ReadAsync<LoanResponse>();
        repaid.Status.Should().Be("Repaid");
        repaid.NextPayment.Should().BeNull();

        var walletAfter = await (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}")).ReadAsync<WalletResponse>();
        walletAfter.CurrentBalance.Value().Should().Be(9_700m);
    }

    [Fact]
    public async Task SoftDeletedPayment_ReturnsItemsToUnpaid()
    {
        var (wallet, expenseTypeId, loan) = await ArrangeAsync(200m);
        await (await Client.PostAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule/generate",
            new { FirstPaymentDate = Today.AddMonths(1), NumberOfPayments = 2 })).ReadAsync<LoanResponse>();
        var paid = await (await Client.PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 100m)).ReadAsync<LoanResponse>();
        paid.Schedule[0].Status.Should().Be("Paid");

        (await Client.DeleteAsync($"/api/v1/operations/{paid.Payments.Single().OperationId}")).EnsureSuccessStatusCode();

        var after = await (await Client.GetAsync($"/api/v1/loans/{loan.Id}")).ReadAsync<LoanResponse>();
        after.Schedule.Select(x => x.Status).Should().Equal("Expected", "Expected");
        after.PaidAmount.Value().Should().Be(0m);
        after.Payments.Single().IsCounted.Should().BeFalse();
    }

    [Fact]
    public async Task EditSchedule_RescheduleDate_IsAudited_AndTermsCanChange()
    {
        var (_, _, loan) = await ArrangeAsync(200m);
        var creditor = await Client.CreateCreditorAsync("Другой банк");
        await (await Client.PostAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule/generate",
            new { FirstPaymentDate = Today.AddMonths(1), NumberOfPayments = 2 })).ReadAsync<LoanResponse>();

        var moved = await (await Client.PutAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule", new
        {
            Items = new[] { new { DueDate = Today.AddMonths(2), Amount = 100m }, new { DueDate = Today.AddMonths(3), Amount = 150m } },
        })).ReadAsync<LoanResponse>();
        moved.Schedule.Select(x => x.DueDate).Should().Equal(Today.AddMonths(2), Today.AddMonths(3));
        moved.ScheduledAmount.Value().Should().Be(250m);

        var updated = await (await Client.PatchAsJsonAsync($"/api/v1/loans/{loan.Id}", new
        {
            CreditorId = creditor.Id, Name = "Ипотека", AmountToRepay = 250m, StartDate = Today,
        })).ReadAsync<LoanResponse>();
        updated.AmountToRepay.Value().Should().Be(250m);
        updated.CreditorId.Should().Be(creditor.Id);

        (await Client.PutAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule", new { Items = new[] { new { DueDate = Today, Amount = 0m } } }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var entries = await (await Client.GetAsync($"/api/v1/audit-entries?entityType=Loan&entityId={loan.Id}&limit=50")).ReadAsync<AuditEntryPageResponse>();
        entries.Data.Select(e => e.Action).Should().BeEquivalentTo(["Created", "ScheduleChanged", "ScheduleChanged", "Updated"]);
        entries.Data.Where(e => e.Action == "ScheduleChanged")
            .Should().Contain(e => e.Changes.Any(c => c.Field == "Schedule" && c.OldValue != null && c.NewValue != null));
    }

    [Fact]
    public async Task Lifecycle_DeleteWithScheduleOnly_CancelWithPayments()
    {
        var (wallet, expenseTypeId, loan) = await ArrangeAsync(100m);
        await (await Client.PostAsJsonAsync($"/api/v1/loans/{loan.Id}/schedule/generate",
            new { FirstPaymentDate = Today.AddMonths(1), NumberOfPayments = 2 })).ReadAsync<LoanResponse>();
        (await Client.DeleteAsync($"/api/v1/loans/{loan.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var creditor = await Client.CreateCreditorAsync("Ещё банк");
        var withPayment = await Client.CreateLoanAsync(creditor.Id, wallet.CurrencyId, 100m);
        (await Client.PayAsync($"/api/v1/loans/{withPayment.Id}/payments", wallet.Id, expenseTypeId, 10m)).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/v1/loans/{withPayment.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancelled = await (await Client.PostAsync($"/api/v1/loans/{withPayment.Id}/cancel", null)).ReadAsync<LoanResponse>();
        cancelled.Status.Should().Be("Cancelled");
        cancelled.NextPayment.Should().BeNull();
        (await Client.PayAsync($"/api/v1/loans/{withPayment.Id}/payments", wallet.Id, expenseTypeId, 10m)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var list = await (await Client.GetAsync("/api/v1/loans")).ReadAsync<LoanPage>();
        list.Data.Should().ContainSingle(l => l.Id == withPayment.Id);
    }

    [Fact]
    public async Task ConcurrentPayments_DoNotExceedRemaining()
    {
        var (wallet, expenseTypeId, loan) = await ArrangeAsync(100m);

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Fixture.Factory.CreateClient().WithToken(Fixture.Token).PayAsync($"/api/v1/loans/{loan.Id}/payments", wallet.Id, expenseTypeId, 60m)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Where(r => r.StatusCode != HttpStatusCode.Created).Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Conflict);

        var after = await (await Client.GetAsync($"/api/v1/loans/{loan.Id}")).ReadAsync<LoanResponse>();
        after.PaidAmount.Value().Should().Be(60m);
    }
}

internal static class AuthorizedClient
{
    public static HttpClient WithToken(this HttpClient client, string token)
    {
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}
