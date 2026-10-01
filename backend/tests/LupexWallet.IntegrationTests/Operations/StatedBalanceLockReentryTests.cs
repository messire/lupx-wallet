using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Smoke test of the stated-balance flow: operation commands take the wallet lock that the
/// BalanceHistory cascade (same transaction, same connection) takes again, so a wrong re-entry
/// would hang instead of failing. Replays the example of ADR-0003, §6 through the API.
/// </summary>
public sealed class StatedBalanceLockReentryTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly TimeSpan HangGuard = TimeSpan.FromSeconds(60);

    private async Task<decimal?> CorrectionAmountAsync(Guid walletId, DateOnly date)
    {
        var response = await Client.GetAsync(
            $"/api/v1/operations?walletId={walletId}&dateFrom={date:yyyy-MM-dd}&dateTo={date:yyyy-MM-dd}&limit=100");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<JsonElement>();
        var correction = page.GetProperty("data").EnumerateArray()
            .Where(x => x.GetProperty("isCorrection").GetBoolean())
            .Select(x => (decimal?)decimal.Parse(x.GetProperty("amount").GetProperty("amount").GetString()!, CultureInfo.InvariantCulture))
            .SingleOrDefault();
        return correction;
    }

    [Fact]
    public async Task OperationInWalletWithStatedBalance_DoesNotHangAndKeepsStatedBalances()
    {
        await AuthenticateAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: today.AddDays(-60));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense");

        var first = today.AddDays(-40);
        var tenth = today.AddDays(-30);
        var fifteenth = today.AddDays(-25);
        var twentieth = today.AddDays(-20);

        var firstIncomeId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 500m, first);
        await Client.CreateOperationReturningIdAsync(wallet.Id, expenseTypeId, 200m, tenth);
        var (tenthStatus, tenthCorrection) = await Client.SetStatedBalanceAsync(wallet.Id, tenth, 350m)
            .WaitAsync(HangGuard);
        tenthStatus.Should().Be(HttpStatusCode.Created);
        tenthCorrection.IsCorrection.Should().BeTrue();
        tenthCorrection.Amount.Amount.Should().Be("50");

        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 400m, twentieth);
        var (_, twentiethCorrection) = await Client.SetStatedBalanceAsync(wallet.Id, twentieth, 600m).WaitAsync(HangGuard);
        twentiethCorrection.Amount.Amount.Should().Be("-150");
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, twentieth)).Should().Be(600m);

        // −100 dated the 15th: the correction of the 20th absorbs it, the balance on the 20th stays 600.
        await Client.CreateOperationReturningIdAsync(wallet.Id, expenseTypeId, 100m, fifteenth).WaitAsync(HangGuard);
        (await CorrectionAmountAsync(wallet.Id, twentieth)).Should().Be(-50m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, twentieth)).Should().Be(600m);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(600m);

        // The first income edited to +600: the correction of the 10th becomes −50, later balances do not change.
        var patch = await Client.PatchAsJsonAsync($"/api/v1/operations/{firstIncomeId}", new
        {
            WalletId = wallet.Id,
            OperationTypeId = incomeTypeId,
            Amount = 600m,
            OperationDate = first,
        }).WaitAsync(HangGuard);
        patch.EnsureSuccessStatusCode();

        (await CorrectionAmountAsync(wallet.Id, tenth)).Should().Be(-50m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, tenth)).Should().Be(350m);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, twentieth)).Should().Be(600m);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(600m);
    }
}
