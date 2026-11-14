using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Debts;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Funds;

/// <summary>
/// Review v0.4.0 through the HTTP API: R3 — restoring a repayment of a debt already repaid by another payment takes no
/// reserve of the linked fund; R8 — deleting a repayment gives no reserve back to an archived wallet.
/// </summary>
public sealed class LinkedReserveReviewFixesApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed record Fund(Guid Id, MoneyResponse Reserved);

    private async Task<Fund> CreateLinkedFundAsync(Guid currencyId, Guid debtId)
    {
        var response = await Client.PostAsJsonAsync("/api/v1/funds", new
        {
            Name = "На долг", TargetAmount = "5000", CurrencyId = currencyId, LinkedDebtId = debtId,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<Fund>())!;
    }

    private async Task ReserveAsync(Guid fundId, Guid walletId, decimal amount)
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/v1/funds/{fundId}/reserve", new { WalletId = walletId, Amount = amount.ToString(CultureInfo.InvariantCulture) });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
    }

    private async Task<decimal> ReservedAsync(Guid fundId) =>
        (await Client.GetFromJsonAsync<Fund>($"/api/v1/funds/{fundId}"))!.Reserved.Value();

    private async Task<Guid> RepayAsync(Guid debtId, Guid walletId, Guid typeId, decimal amount)
    {
        var debt = await (await Client.PayAsync($"/api/v1/debts/{debtId}/repayments", walletId, typeId, amount)).ReadAsync<DebtResponse>();
        return debt.Repayments.Single(r => r.IsCounted).OperationId; // the other ones are deleted here
    }

    [Fact]
    public async Task RestoreRepayment_DebtAlreadyRepaidByAnotherPayment_ReleasesNoReserve()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(1000m, name: "Основной");
        var creditor = await Client.CreateCreditorAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 100m);
        var fund = await CreateLinkedFundAsync(wallet.CurrencyId, debt.Id);
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", "Погашение");

        var first = await RepayAsync(debt.Id, wallet.Id, typeId, 100m);
        (await Client.DeleteAsync($"/api/v1/operations/{first}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        await ReserveAsync(fund.Id, wallet.Id, 100m);
        await RepayAsync(debt.Id, wallet.Id, typeId, 100m);
        (await ReservedAsync(fund.Id)).Should().Be(0m, "the second repayment released the reserve");

        // Money set aside again, e.g. for an increase of the debt.
        await ReserveAsync(fund.Id, wallet.Id, 50m);

        (await Client.PostAsync($"/api/v1/operations/{first}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await ReservedAsync(fund.Id)).Should().Be(50m, "the debt needed nothing more before the restore: no reserve is taken for it");
        var after = await Client.GetFromJsonAsync<DebtResponse>($"/api/v1/debts/{debt.Id}");
        after!.Status.Should().Be("Repaid");
        after.RemainingAmount.Value().Should().Be(0m);
    }

    [Fact]
    public async Task DeleteRepayment_FromArchivedWallet_GivesNoReserveBack()
    {
        await AuthenticateAsync();
        await Client.CreateWalletAsync(0m, name: "Основной");
        var wallet = await Client.CreateWalletAsync(500m, name: "Накопления");
        var creditor = await Client.CreateCreditorAsync();
        var debt = await Client.CreateDebtAsync(creditor.Id, wallet.CurrencyId, 200m);
        var fund = await CreateLinkedFundAsync(wallet.CurrencyId, debt.Id);
        var typeId = await Client.CreateOperationTypeIdAsync("Expense", "Погашение");
        await ReserveAsync(fund.Id, wallet.Id, 200m);

        var payment = await RepayAsync(debt.Id, wallet.Id, typeId, 200m);
        (await ReservedAsync(fund.Id)).Should().Be(0m);
        var archived = await Client.PostAsync($"/api/v1/wallets/{wallet.Id}/archive", null);
        archived.StatusCode.Should().Be(HttpStatusCode.OK, await archived.Content.ReadAsStringAsync());

        (await Client.DeleteAsync($"/api/v1/operations/{payment}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await ReservedAsync(fund.Id)).Should().Be(0m, "an archived wallet never holds a reserve (ADR-0010 §4)");
    }
}
