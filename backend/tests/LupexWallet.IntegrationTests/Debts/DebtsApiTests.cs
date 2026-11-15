using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Audit;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Debts;

/// <summary>
/// Debts module end-to-end (ADR-0010 §3, §8; issue #6): creditors, debts, repayment as a real
/// expense created atomically, soft-deleted repayment stops counting, summary, lifecycle, audit.
/// </summary>
public sealed class DebtsApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private async Task<(WalletResponse Wallet, Guid ExpenseTypeId, CreditorResponse Creditor)> ArrangeAsync(decimal balance = 1000m)
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: balance);
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense", "Погашение долга");
        var creditor = await Client.CreateCreditorAsync("Иван");
        return (wallet, expenseTypeId, creditor);
    }

    [Fact]
    public async Task Creditor_CreateRenameList_DeleteUnused_ConflictWhenUsed_DeactivatedNotUsableForNewDebt()
    {
        var (wallet, _, used) = await ArrangeAsync();
        var unused = await Client.CreateCreditorAsync("Лишний");

        var renamed = await (await Client.PatchAsJsonAsync($"/api/v1/creditors/{used.Id}", new { Name = "Пётр" })).ReadAsync<CreditorResponse>();
        renamed.Name.Should().Be("Пётр");

        await Client.CreateDebtAsync(used.Id, wallet.CurrencyId, 10m);

        (await Client.DeleteAsync($"/api/v1/creditors/{unused.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.DeleteAsync($"/api/v1/creditors/{used.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var deactivated = await (await Client.PostAsync($"/api/v1/creditors/{used.Id}/deactivate", null)).ReadAsync<CreditorResponse>();
        deactivated.IsActive.Should().BeFalse();

        var active = await (await Client.GetAsync("/api/v1/creditors")).ReadAsync<CreditorPage>();
        active.Data.Should().BeEmpty();
        var all = await (await Client.GetAsync("/api/v1/creditors?includeInactive=true")).ReadAsync<CreditorPage>();
        all.Data.Should().ContainSingle(c => c.Id == used.Id);

        var newDebt = await Client.PostAsJsonAsync("/api/v1/debts", new
        {
            CreditorId = used.Id, Name = "Ещё", CurrencyId = wallet.CurrencyId, OriginalAmount = 5m, StartDate = DebtsTestHelpers.Today,
        });
        newDebt.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Repayment_PartialThenFull_CreatesExpenseOperations_ChangesWalletBalance_AndRepaysDebt()
    {
        var (wallet, expenseTypeId, creditor) = await ArrangeAsync(balance: 1000m);
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 500m);
        debt.Status.Should().Be("Active");

        var partial = await (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 200m)).ReadAsync<DebtResponse>();
        partial.RemainingAmount.Value().Should().Be(300m);
        partial.RepaidAmount.Value().Should().Be(200m);
        partial.Status.Should().Be("Active");
        var operationId = partial.Repayments.Should().ContainSingle().Subject.OperationId;

        var operation = await (await Client.GetAsync($"/api/v1/operations/{operationId}")).ReadAsync<OperationProbe>();
        operation.OperationTypeId.Should().Be(expenseTypeId);
        operation.Amount.Value().Should().Be(200m);

        var overpay = await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 300.01m);
        overpay.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var full = await (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 300m)).ReadAsync<DebtResponse>();
        full.Status.Should().Be("Repaid");
        full.RemainingAmount.Value().Should().Be(0m);
        full.Repayments.Should().HaveCount(2);

        var walletAfter = await (await Client.GetAsync($"/api/v1/wallets/{wallet.Id}")).ReadAsync<WalletResponse>();
        walletAfter.CurrentBalance.Value().Should().Be(500m, "both repayments are real expenses of the wallet");

        (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 1m)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "nothing is left to repay");
    }

    [Fact]
    public async Task SoftDeletedRepaymentOperation_StopsCounting_RestoredCountsAgain()
    {
        var (wallet, expenseTypeId, creditor) = await ArrangeAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 100m);
        var repaid = await (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 100m)).ReadAsync<DebtResponse>();
        repaid.Status.Should().Be("Repaid");
        var operationId = repaid.Repayments.Single().OperationId;

        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDelete = await (await Client.GetAsync($"/api/v1/debts/{debt.Id}")).ReadAsync<DebtResponse>();
        afterDelete.Status.Should().Be("Active");
        afterDelete.RemainingAmount.Value().Should().Be(100m);
        afterDelete.Repayments.Single().Should().Match<PaymentResponse>(r => !r.IsCounted && r.IsOperationDeleted);

        (await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null)).EnsureSuccessStatusCode();
        var afterRestore = await (await Client.GetAsync($"/api/v1/debts/{debt.Id}")).ReadAsync<DebtResponse>();
        afterRestore.Status.Should().Be("Repaid");
        afterRestore.Repayments.Single().IsCounted.Should().BeTrue();
    }

    [Fact]
    public async Task Repayment_Validation_CurrencyMismatchIs409_NonExpenseTypeAndMissingFieldsAre400_UnknownDebtIs404()
    {
        var (wallet, expenseTypeId, creditor) = await ArrangeAsync();
        var otherWallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, name: "Другой");
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income", "Доход");
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 100m);
        var url = $"/api/v1/debts/{debt.Id}/repayments";

        (await Client.PayAsync(url, otherWallet.Id, expenseTypeId, 10m)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.PayAsync(url, wallet.Id, incomeTypeId, 10m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PayAsync(url, wallet.Id, expenseTypeId, 0m)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PayAsync(url, wallet.Id, expenseTypeId, 10m, DebtsTestHelpers.Today.AddDays(1))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PostAsJsonAsync(url, new { WalletId = wallet.Id })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.PayAsync($"/api/v1/debts/{Guid.NewGuid()}/repayments", wallet.Id, expenseTypeId, 10m)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var operations = await (await Client.GetAsync($"/api/v1/operations?walletId={wallet.Id}")).ReadAsync<OperationProbePage>();
        operations.Data.Should().BeEmpty("rejected repayments must not leave an expense behind");
    }

    [Fact]
    public async Task Lifecycle_DeleteWithoutRepayments_CancelWithRepayments()
    {
        var (wallet, expenseTypeId, creditor) = await ArrangeAsync();
        var empty = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 50m);
        (await Client.DeleteAsync($"/api/v1/debts/{empty.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetAsync($"/api/v1/debts/{empty.Id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var withRepayment = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 50m);
        (await Client.PayAsync($"/api/v1/debts/{withRepayment.Id}/repayments", wallet.Id, expenseTypeId, 10m)).EnsureSuccessStatusCode();
        (await Client.DeleteAsync($"/api/v1/debts/{withRepayment.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);

        var cancelled = await (await Client.PostAsync($"/api/v1/debts/{withRepayment.Id}/cancel", null)).ReadAsync<DebtResponse>();
        cancelled.Status.Should().Be("Cancelled");
        (await Client.PayAsync($"/api/v1/debts/{withRepayment.Id}/repayments", wallet.Id, expenseTypeId, 10m)).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await Client.PatchAsJsonAsync($"/api/v1/debts/{withRepayment.Id}", new
        {
            CreditorId = creditor.Id, Name = "x", OriginalAmount = 60m, StartDate = DebtsTestHelpers.Today,
        })).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Update_ChangesTerms()
    {
        var (wallet, _, creditor) = await ArrangeAsync();
        var other = await Client.CreateCreditorAsync("Мария");
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 50m);

        var updated = await (await Client.PatchAsJsonAsync($"/api/v1/debts/{debt.Id}", new
        {
            CreditorId = other.Id, Name = "Новое имя", OriginalAmount = "75.5", StartDate = DebtsTestHelpers.Today,
        })).ReadAsync<DebtResponse>();

        updated.CreditorId.Should().Be(other.Id);
        updated.Name.Should().Be("Новое имя");
        updated.OriginalAmount.Amount.Should().Be("75.5");
    }

    [Fact]
    public async Task Summary_TotalAndBreakdownByCreditor_PerCurrency_ExcludesCancelledAndRepaid()
    {
        var (wallet, expenseTypeId, ivan) = await ArrangeAsync();
        var otherCurrencyWallet = await Client.CreateWalletAsync(name: "EUR");
        var maria = await Client.CreateCreditorAsync("Мария");
        var bank = await Client.CreateCreditorAsync("Банк");
        var olga = await Client.CreateCreditorAsync("Ольга");

        var ivanDebt = await Client.CreateDebtAsync(ivan.Id, wallet.CurrencyId, 300m);
        await Client.CreateDebtAsync(ivan.Id, wallet.CurrencyId, 200m);
        await Client.CreateDebtAsync(maria.Id, wallet.CurrencyId, 100m);
        await Client.CreateDebtAsync(bank.Id, otherCurrencyWallet.CurrencyId, 40m);
        var cancelled = await Client.CreateDebtAsync(olga.Id, wallet.CurrencyId, 999m);
        (await Client.PostAsync($"/api/v1/debts/{cancelled.Id}/cancel", null)).EnsureSuccessStatusCode();
        var repaid = await Client.CreateDebtAsync(olga.Id, wallet.CurrencyId, 5m);
        (await Client.PayAsync($"/api/v1/debts/{repaid.Id}/repayments", wallet.Id, expenseTypeId, 5m)).EnsureSuccessStatusCode();
        (await Client.PayAsync($"/api/v1/debts/{ivanDebt.Id}/repayments", wallet.Id, expenseTypeId, 50m)).EnsureSuccessStatusCode();

        var summary = await (await Client.GetAsync("/api/v1/debts/summary")).ReadAsync<DebtSummary>();

        summary.Currencies.Should().HaveCount(2);
        var main = summary.Currencies.Single(c => c.CurrencyId == wallet.CurrencyId);
        main.TotalRemainingAmount.Value().Should().Be(550m);
        main.DebtCount.Should().Be(3);
        main.Creditors.Select(c => (c.CreditorName, c.RemainingAmount.Value(), c.DebtCount))
            .Should().Equal(("Иван", 450m, 2), ("Мария", 100m, 1));
        summary.Currencies.Single(c => c.CurrencyId == otherCurrencyWallet.CurrencyId).TotalRemainingAmount.Value().Should().Be(40m);

        var list = await (await Client.GetAsync($"/api/v1/debts?creditorId={ivan.Id}")).ReadAsync<DebtPage>();
        list.Data.Should().HaveCount(2);
    }

    [Fact]
    public async Task List_PaginatesByCursor()
    {
        var (wallet, _, creditor) = await ArrangeAsync();
        for (var i = 0; i < 3; i++)
        {
            await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 10m + i);
        }

        var first = await (await Client.GetAsync("/api/v1/debts?limit=2")).ReadAsync<DebtPage>();
        first.Data.Should().HaveCount(2);
        first.Pagination.HasMore.Should().BeTrue();
        var second = await (await Client.GetAsync($"/api/v1/debts?limit=2&cursor={Uri.EscapeDataString(first.Pagination.NextCursor!)}")).ReadAsync<DebtPage>();
        second.Data.Should().ContainSingle();
        second.Pagination.HasMore.Should().BeFalse();
    }

    [Fact]
    public async Task CurrencyUsedByDebt_CannotBeRemovedFromActive()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("ZDB", "Валюта долга");
        var creditor = await Client.CreateCreditorAsync();
        await Client.CreateDebtAsync(creditor.Id, currency.Id, 10m);

        (await Client.DeleteAsync($"/api/v1/currencies/{currency.Id}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Audit_RecordsDebtLifecycleAndRepaymentInTheDebtRecord()
    {
        var (wallet, expenseTypeId, creditor) = await ArrangeAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 100m);
        var repaid = await (await Client.PayAsync($"/api/v1/debts/{debt.Id}/repayments", wallet.Id, expenseTypeId, 30m)).ReadAsync<DebtResponse>();
        (await Client.PostAsync($"/api/v1/debts/{debt.Id}/cancel", null)).EnsureSuccessStatusCode();

        var entries = await (await Client.GetAsync($"/api/v1/audit-entries?entityType=Debt&entityId={debt.Id}&limit=50")).ReadAsync<AuditEntryPageResponse>();
        entries.Data.Select(e => e.Action).Should().BeEquivalentTo(["Created", "RepaymentRecorded", "Cancelled"]);
        var repayment = entries.Data.Single(e => e.Action == "RepaymentRecorded");
        repayment.Changes.Should().Contain(c => c.Field == "Repayment.OperationId" && c.NewValue == repaid.Repayments.Single().OperationId.ToString());
        repayment.Changes.Should().Contain(c => c.Field == "Repayment.Amount" && c.NewValue == "30");

        var creditorEntries = await (await Client.GetAsync($"/api/v1/audit-entries?entityType=Creditor&entityId={creditor.Id}")).ReadAsync<AuditEntryPageResponse>();
        creditorEntries.Data.Should().ContainSingle(e => e.Action == "Created");
    }

    private sealed record OperationProbe(Guid Id, Guid? OperationTypeId, MoneyResponse Amount);
    private sealed record OperationProbePage(List<OperationProbe> Data);
}
