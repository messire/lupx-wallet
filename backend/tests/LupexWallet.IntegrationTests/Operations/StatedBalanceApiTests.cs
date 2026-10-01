using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// PUT /wallets/{id}/stated-balances/{date} and the corrections it creates (ADR-0003, §6; docs/api/openapi.yaml).
/// Every step also checks the invariant CurrentBalance = InitialBalance + sum of AppliedDelta straight from the
/// tables, the balance on dates and the snapshots. All dates are relative to today (a date in the future is rejected).
/// </summary>
public sealed class StatedBalanceApiTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => RecordLifecycleTestHelpers.Today;

    private sealed record Fx(WalletResponse Wallet, Guid IncomeTypeId, Guid ExpenseTypeId);

    private async Task<Fx> CreateWalletAsync(decimal initialBalance = 0m, int startDaysAgo = 60)
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: initialBalance, accountingStartDate: Today.AddDays(-startDaysAgo));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var expenseTypeId = await Client.CreateOperationTypeIdAsync("Expense");
        return new Fx(wallet, incomeTypeId, expenseTypeId);
    }

    private async Task<HttpResponseMessage> PatchAsync(Guid operationId, Guid walletId, Guid typeId, decimal amount, DateOnly date) =>
        await Client.PatchAsJsonAsync($"/api/v1/operations/{operationId}", new
        {
            WalletId = walletId, OperationTypeId = typeId, Amount = amount, OperationDate = date, AdjustmentMode = (string?)null,
        });

    private async Task<OperationResponse> GetOperationAsync(Guid id)
    {
        var response = await Client.GetAsync($"/api/v1/operations/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
    }

    private async Task<decimal> CorrectionOnAsync(Guid walletId, DateOnly date)
    {
        var amount = await Fixture.CorrectionAmountAsync(walletId, date);
        amount.Should().NotBeNull($"a correction exists on {date}");
        return amount!.Value;
    }

    private async Task<decimal> SnapshotAsync(Guid walletId, DateOnly date) =>
        decimal.Parse(
            (await Client.GetSnapshotsAsync(walletId, date, date)).Should().ContainSingle($"a snapshot exists for {date}").Subject.Balance.Amount,
            CultureInfo.InvariantCulture);

    // ---- The example of ADR-0003, §6: every step with the invariant, balances on dates, snapshots, audit ----

    [Fact]
    public async Task AdrExample_AllStepsKeepStatedBalancesInvariantAndSnapshotsConsistent()
    {
        var setup = await CreateWalletAsync();
        var walletId = setup.Wallet.Id;
        var first = Today.AddDays(-40);
        var tenth = Today.AddDays(-30);
        var fifteenth = Today.AddDays(-25);
        var twentieth = Today.AddDays(-20);

        var incomeId = await Client.CreateOperationReturningIdAsync(walletId, setup.IncomeTypeId, 500m, first);
        await Client.CreateOperationReturningIdAsync(walletId, setup.ExpenseTypeId, 200m, tenth);
        await Fixture.AssertWalletInvariantAsync(walletId, "after the plain operations");

        // 10th: 350 stated, 300 by the operations -> +50 ("unknown income").
        var (status, tenthCorrection) = await Client.SetStatedBalanceAsync(walletId, tenth, 350m);
        status.Should().Be(HttpStatusCode.Created);
        tenthCorrection.Amount.Amount.Should().Be("50");
        tenthCorrection.StatedBalance!.Amount.Should().Be("350");
        (await Client.GetWalletBalanceOnDateAsync(walletId, tenth)).Should().Be(350m);
        (await SnapshotAsync(walletId, tenth)).Should().Be(350m);
        (await Client.GetWalletBalanceOnDateAsync(walletId, first)).Should().Be(500m, "dates before the stated one do not change");
        (await Client.GetWalletBalanceAsync(walletId)).Should().Be(350m);
        await Fixture.AssertWalletInvariantAsync(walletId, "after the first statement");

        // 20th: +400, 600 stated -> -150 ("unknown expense").
        await Client.CreateOperationReturningIdAsync(walletId, setup.IncomeTypeId, 400m, twentieth);
        (await Client.GetWalletBalanceAsync(walletId)).Should().Be(750m, "the 20th is not stated yet");
        var (_, twentiethCorrection) = await Client.SetStatedBalanceAsync(walletId, twentieth, 600m);
        twentiethCorrection.Amount.Amount.Should().Be("-150");
        (await Client.GetWalletBalanceOnDateAsync(walletId, twentieth)).Should().Be(600m);
        (await SnapshotAsync(walletId, twentieth)).Should().Be(600m);
        (await Client.GetWalletBalanceAsync(walletId)).Should().Be(600m);
        await Fixture.AssertWalletInvariantAsync(walletId, "after the second statement");

        // -100 on the 15th: the correction of the 20th becomes -50, the balance of the 20th stays 600.
        await Client.CreateOperationReturningIdAsync(walletId, setup.ExpenseTypeId, 100m, fifteenth);
        (await CorrectionOnAsync(walletId, tenth)).Should().Be(50m, "an operation dated after the 10th does not change it");
        (await CorrectionOnAsync(walletId, twentieth)).Should().Be(-50m);
        (await Client.GetWalletBalanceOnDateAsync(walletId, fifteenth)).Should().Be(250m);
        (await SnapshotAsync(walletId, fifteenth)).Should().Be(250m);
        (await Client.GetWalletBalanceOnDateAsync(walletId, twentieth)).Should().Be(600m);
        (await SnapshotAsync(walletId, twentieth)).Should().Be(600m);
        (await Client.GetWalletBalanceAsync(walletId)).Should().Be(600m, "a stated balance later in time absorbs the new operation");
        await Fixture.AssertWalletInvariantAsync(walletId, "after the expense of the 15th");

        // The income of the 1st edited to +600: the correction of the 10th flips to -50, balances from the 10th do not change.
        (await PatchAsync(incomeId, walletId, setup.IncomeTypeId, 600m, first)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorrectionOnAsync(walletId, tenth)).Should().Be(-50m);
        (await CorrectionOnAsync(walletId, twentieth)).Should().Be(-50m);
        (await Client.GetWalletBalanceOnDateAsync(walletId, first)).Should().Be(600m, "the edited day itself changes");
        (await Client.GetWalletBalanceOnDateAsync(walletId, tenth)).Should().Be(350m);
        (await SnapshotAsync(walletId, first)).Should().Be(600m);
        (await SnapshotAsync(walletId, tenth)).Should().Be(350m);
        (await SnapshotAsync(walletId, fifteenth)).Should().Be(250m);
        (await SnapshotAsync(walletId, twentieth)).Should().Be(600m);
        (await Client.GetWalletBalanceAsync(walletId)).Should().Be(600m);
        await Fixture.AssertWalletInvariantAsync(walletId, "after the edit of the first income");

        // Rows by sign in the list: the sign of the amount decides "unknown income" or "unknown expense".
        var listed = await Client.ListOperationsAsync(walletId);
        listed.Single(x => x.IsCorrection && x.OperationDate == tenth).Amount.ParseAmount().Should().Be(-50m);
        listed.Single(x => x.IsCorrection && x.OperationDate == twentieth).Amount.ParseAmount().Should().Be(-50m);

        // Audit: one entry per correction per command (Created on the statement, Recalculated on the system change).
        var tenthAudit = await Client.GetAuditEntriesAsync("Operation", tenthCorrection.Id);
        tenthAudit.Select(e => e.Action).Should().BeEquivalentTo("Created", "Recalculated");
        var twentiethAudit = await Client.GetAuditEntriesAsync("Operation", twentiethCorrection.Id);
        twentiethAudit.Select(e => e.Action).Should().BeEquivalentTo("Created", "Recalculated");
        twentiethAudit.Should().OnlyContain(e => e.ActorKind == "User");
    }

    // ---- 201 / 200 / the row in the list ----

    [Fact]
    public async Task Put_FirstStatement_Returns201WithCorrectionRowLastInItsDay()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 500m, day);
        var expenseId = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.ExpenseTypeId, 200m, day);

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/wallets/{setup.Wallet.Id}/stated-balances/{day:yyyy-MM-dd}", new { Amount = 350m });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        response.Headers.Location.Should().NotBeNull();
        var body = (await response.Content.ReadFromJsonAsync<OperationResponse>())!;
        body.IsCorrection.Should().BeTrue();
        body.StatedBalance!.Amount.Should().Be("350");
        body.StatedBalance.CurrencyId.Should().Be(setup.Wallet.CurrencyId);
        body.Amount.Amount.Should().Be("50");
        body.OperationTypeId.Should().BeNull();
        body.AdjustmentMode.Should().BeNull();
        body.TransferId.Should().BeNull();
        body.IsDeleted.Should().BeFalse();

        var listed = await Client.ListOperationsAsync(setup.Wallet.Id);
        listed.Select(x => x.OperationDate).Should().BeInAscendingOrder();
        listed.Where(x => x.OperationDate == day).Select(x => x.IsCorrection).Should().Equal(new[] { false, false, true }, "the correction is the last row of its day");
        listed.Last().Id.Should().Be(body.Id);
        listed.Single(x => x.Id == expenseId).IsCorrection.Should().BeFalse();
        listed.Where(x => !x.IsCorrection).Should().OnlyContain(x => x.StatedBalance == null);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    [Fact]
    public async Task Put_RepeatedStatement_Returns200KeepsTheIdAndReplacesTheValue()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 300m, day);
        var (firstStatus, first) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 350m);

        var (secondStatus, second) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 400m);

        firstStatus.Should().Be(HttpStatusCode.Created);
        secondStatus.Should().Be(HttpStatusCode.OK);
        second.Id.Should().Be(first.Id, "the same row is replaced");
        second.StatedBalance!.Amount.Should().Be("400");
        second.Amount.Amount.Should().Be("100");
        (await Fixture.CorrectionRowCountAsync(setup.Wallet.Id)).Should().Be(1);
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, day)).Should().Be(400m);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(400m);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
        (await Client.GetAuditEntriesAsync("Operation", first.Id)).Select(e => e.Action).Should().BeEquivalentTo("Created", "Updated");
    }

    [Fact]
    public async Task Put_SameValueAgain_Returns200WithoutNewAuditEntryOrWalletWrite()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 300m, day);
        var (_, created) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 350m);
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await connection.CountAsync("SELECT count(*) FROM audit.audit_entries");
        var walletXminBefore = await connection.ReadXminsAsync("SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", setup.Wallet.Id));

        var (status, again) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 350m);

        status.Should().Be(HttpStatusCode.OK);
        again.Id.Should().Be(created.Id);
        (await connection.CountAsync("SELECT count(*) FROM audit.audit_entries")).Should().Be(auditBefore, "nothing changed, nothing is audited");
        (await connection.ReadXminsAsync("SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", setup.Wallet.Id)))
            .Should().Equal(walletXminBefore, "the wallet row is not written");
    }

    [Fact]
    public async Task Put_NegativeAmount_IsAccepted()
    {
        var setup = await CreateWalletAsync();

        var (status, correction) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, Today.AddDays(-3), -80m);

        status.Should().Be(HttpStatusCode.Created);
        correction.StatedBalance!.Amount.Should().Be("-80");
        correction.Amount.Amount.Should().Be("-80");
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(-80m);
    }

    [Fact]
    public async Task Put_StatedBalanceWithoutAnyOperation_IsADifferenceToTheInitialBalance()
    {
        var setup = await CreateWalletAsync(initialBalance: 100m);

        var (_, correction) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, Today.AddDays(-3), 70m);

        correction.Amount.Amount.Should().Be("-30");
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(70m);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    // ---- 400 / 404 ----

    [Fact]
    public async Task Put_DateBeforeAccountingStart_Returns400AndCreatesNothing()
    {
        var setup = await CreateWalletAsync(startDaysAgo: 10);

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/wallets/{setup.Wallet.Id}/stated-balances/{Today.AddDays(-11):yyyy-MM-dd}", new { Amount = 10m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Fixture.CorrectionRowCountAsync(setup.Wallet.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Put_DateInTheFuture_Returns400AndCreatesNothing()
    {
        var setup = await CreateWalletAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/wallets/{setup.Wallet.Id}/stated-balances/{Today.AddDays(1):yyyy-MM-dd}", new { Amount = 10m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Fixture.CorrectionRowCountAsync(setup.Wallet.Id)).Should().Be(0);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(0m);
    }

    [Fact]
    public async Task Put_TodayAndAccountingStartDate_AreInsideTheRange()
    {
        var setup = await CreateWalletAsync(startDaysAgo: 10);

        (await Client.SetStatedBalanceAsync(setup.Wallet.Id, Today, 5m)).Status.Should().Be(HttpStatusCode.Created);
        (await Client.SetStatedBalanceAsync(setup.Wallet.Id, Today.AddDays(-10), 7m)).Status.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData("{\"amount\":\"abc\"}")]
    [InlineData("{\"amount\":null}")]
    [InlineData("{}")]                      // a required field is missing: must not silently state a zero balance
    [InlineData("{\"amout\":12.5}")]       // a typo in the field name is the same case
    [InlineData("not json")]
    public async Task Put_InvalidAmount_Returns400(string body)
    {
        var setup = await CreateWalletAsync();

        var response = await Client.PutAsync(
            $"/api/v1/wallets/{setup.Wallet.Id}/stated-balances/{Today:yyyy-MM-dd}",
            new StringContent(body, Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Fixture.CorrectionRowCountAsync(setup.Wallet.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Put_InvalidDateInRoute_Returns400()
    {
        var setup = await CreateWalletAsync();

        var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{setup.Wallet.Id}/stated-balances/not-a-date", new { Amount = 1m });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Put_UnknownWallet_Returns404()
    {
        await AuthenticateAsync();

        var response = await Client.PutAsJsonAsync(
            $"/api/v1/wallets/{Guid.NewGuid()}/stated-balances/{Today:yyyy-MM-dd}", new { Amount = 1m });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Put_WithoutToken_Returns401()
    {
        var response = await Client.PutAsJsonAsync(
            $"/api/v1/wallets/{Guid.NewGuid()}/stated-balances/{Today:yyyy-MM-dd}", new { Amount = 1m });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ---- Zero correction: hidden in the list, still addressable ----

    [Fact]
    public async Task ZeroCorrection_IsHiddenInTheListButReturnedById_AndAppearsWhenItBecomesNonZero()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        var incomeId = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 100m, day);

        var (status, zero) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 100m);

        status.Should().Be(HttpStatusCode.Created);
        zero.Amount.Amount.Should().Be("0");
        (await Client.ListOperationsAsync(setup.Wallet.Id)).Should().NotContain(x => x.IsCorrection, "a zero correction is not shown");
        (await Client.ListOperationsAsync(setup.Wallet.Id)).Should().ContainSingle(x => x.Id == incomeId);
        var byId = await GetOperationAsync(zero.Id);
        byId.IsCorrection.Should().BeTrue();
        byId.StatedBalance!.Amount.Should().Be("100");

        // The income is corrected to 60: the zero correction becomes +40 and shows up.
        (await PatchAsync(incomeId, setup.Wallet.Id, setup.IncomeTypeId, 60m, day)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.ListedCorrectionAsync(setup.Wallet.Id, day))!.Amount.ParseAmount().Should().Be(40m);

        // And disappears again when the discrepancy is gone.
        (await PatchAsync(incomeId, setup.Wallet.Id, setup.IncomeTypeId, 100m, day)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.ListedCorrectionAsync(setup.Wallet.Id, day)).Should().BeNull();
        (await GetOperationAsync(zero.Id)).Amount.Amount.Should().Be("0");
    }

    // ---- Manual actions are forbidden ----

    [Fact]
    public async Task PatchDeleteRestoreOfACorrection_Return409WithoutIdentifiersAndChangeNothing()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 300m, day);
        var (_, correction) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 350m);

        // Even an invalid body is answered with the "calculated by the system" conflict: that check goes first.
        var patch = await PatchAsync(correction.Id, setup.Wallet.Id, setup.ExpenseTypeId, 1m, day);
        var delete = await Client.DeleteAsync($"/api/v1/operations/{correction.Id}");
        var restore = await Client.PostAsync($"/api/v1/operations/{correction.Id}/restore", null);

        foreach (var response in new[] { patch, delete, restore })
        {
            response.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
            var detail = problem.GetProperty("detail").GetString()!;
            detail.Should().Contain("корректирующая сумма");
            detail.Should().NotContain(correction.Id.ToString()).And.NotContain(setup.Wallet.Id.ToString());
            detail.Should().NotContain("/api/").And.NotContain("ADR");
        }

        var unchanged = await GetOperationAsync(correction.Id);
        unchanged.IsDeleted.Should().BeFalse();
        unchanged.Amount.Amount.Should().Be("50");
        unchanged.OperationTypeId.Should().BeNull();
        unchanged.WalletId.Should().Be(setup.Wallet.Id);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    [Fact]
    public async Task PatchOfACorrectionMovingItToAnotherWallet_Returns409AndLeavesBothWalletsAlone()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("SB" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletA = await Client.CreateWalletAsync(0m, Today.AddDays(-20), "A", currencyId: currency.Id);
        var walletB = await Client.CreateWalletAsync(0m, Today.AddDays(-20), "B", currencyId: currency.Id);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var (_, correction) = await Client.SetStatedBalanceAsync(walletA.Id, Today.AddDays(-5), 100m);

        var response = await PatchAsync(correction.Id, walletB.Id, incomeTypeId, 100m, Today.AddDays(-5));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetOperationAsync(correction.Id)).WalletId.Should().Be(walletA.Id);
        (await Client.GetWalletBalanceAsync(walletA.Id)).Should().Be(100m);
        (await Client.GetWalletBalanceAsync(walletB.Id)).Should().Be(0m);
    }

    [Fact]
    public async Task AdjustmentModeAbsolute_IsRejectedWith400OnCreateAndUpdate()
    {
        var setup = await CreateWalletAsync(initialBalance: 1000m);
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");
        var existing = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, adjustmentTypeId, 10m, Today.AddDays(-1), "Delta");

        var create = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = adjustmentTypeId, Amount = 500m, OperationDate = Today, AdjustmentMode = "Absolute",
        });
        var update = await Client.PatchAsJsonAsync($"/api/v1/operations/{existing}", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = adjustmentTypeId, Amount = 500m, OperationDate = Today.AddDays(-1), AdjustmentMode = "Absolute",
        });

        foreach (var response in new[] { create, update })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()
                .Should().Contain("Указать баланс на дату");
        }

        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(1010m, "the rejected requests changed nothing");
    }

    [Fact]
    public async Task PatchOfACorrectionWithAbsoluteAdjustmentMode_Returns409NotTheModeError()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 300m, day);
        var (_, correction) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 350m);

        var response = await Client.PatchAsJsonAsync($"/api/v1/operations/{correction.Id}", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = setup.IncomeTypeId, Amount = 1m, OperationDate = day, AdjustmentMode = "Absolute",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict, "the correction check goes before the check of the mode");
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString().Should().Contain("корректирующая сумма");
    }

    [Fact]
    public async Task PatchOfAMissingOperationWithAbsoluteAdjustmentMode_Returns404()
    {
        var setup = await CreateWalletAsync();

        var response = await Client.PatchAsJsonAsync($"/api/v1/operations/{Guid.NewGuid()}", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = setup.IncomeTypeId, Amount = 1m, OperationDate = Today, AdjustmentMode = "Absolute",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    [InlineData("5")]
    [InlineData("-1")]
    [InlineData("Delta, Delta")]
    [InlineData("Bogus")]
    public async Task NonNamedOrUnknownAdjustmentMode_IsRejectedWith400OnCreateAndUpdate(string mode)
    {
        var setup = await CreateWalletAsync(initialBalance: 1000m);
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");
        var existing = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, adjustmentTypeId, 10m, Today.AddDays(-1), "Delta");

        var create = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = adjustmentTypeId, Amount = 500m, OperationDate = Today, AdjustmentMode = mode,
        });
        var update = await Client.PatchAsJsonAsync($"/api/v1/operations/{existing}", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = adjustmentTypeId, Amount = 500m, OperationDate = Today.AddDays(-1), AdjustmentMode = mode,
        });

        create.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        update.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(1010m, "the rejected requests changed nothing");
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    [Theory]
    [InlineData("Delta")]
    [InlineData("delta")]
    public async Task AdjustmentModeByName_IsAcceptedCaseInsensitively(string mode)
    {
        var setup = await CreateWalletAsync(initialBalance: 1000m);
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");

        var create = await Client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = setup.Wallet.Id, OperationTypeId = adjustmentTypeId, Amount = -50m, OperationDate = Today, AdjustmentMode = mode,
        });

        create.StatusCode.Should().Be(HttpStatusCode.Created);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(950m);
    }

    [Fact]
    public async Task Put_DateOutOfRange_Returns400WithMessageAboutTheBalanceDate()
    {
        var setup = await CreateWalletAsync(startDaysAgo: 10);

        foreach (var date in new[] { Today.AddDays(-11), Today.AddDays(1) })
        {
            var response = await Client.PutAsJsonAsync($"/api/v1/wallets/{setup.Wallet.Id}/stated-balances/{date:yyyy-MM-dd}", new { Amount = 1m });

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("detail").GetString()!;
            detail.Should().Contain("Дата баланса").And.NotContain("Дата операции");
        }
    }

    [Fact]
    public async Task AdjustmentModeDelta_StillWorksAndIsRecalculatedAgainstALaterStatedBalance()
    {
        var setup = await CreateWalletAsync();
        var adjustmentTypeId = await Client.CreateOperationTypeIdAsync("Adjustment");
        var day = Today.AddDays(-10);
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 100m);
        (await CorrectionOnAsync(setup.Wallet.Id, day)).Should().Be(100m);

        var adjustmentId = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, adjustmentTypeId, -30m, day.AddDays(-2), "Delta");

        (await CorrectionOnAsync(setup.Wallet.Id, day)).Should().Be(130m);
        var adjustment = await GetOperationAsync(adjustmentId);
        adjustment.AdjustmentMode.Should().Be("Delta");
        adjustment.IsCorrection.Should().BeFalse();
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, day)).Should().Be(100m);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    // ---- End of day, several dates ----

    [Fact]
    public async Task EndOfDay_OperationDatedOnTheStatedDateIsAbsorbedAndTheBalanceStaysAsStated()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 500m, day.AddDays(-5));
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.ExpenseTypeId, 200m, day);
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 350m);

        // Entered after the statement, dated the same day: absorbed by the correction (+50 -> +80).
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.ExpenseTypeId, 30m, day);

        (await CorrectionOnAsync(setup.Wallet.Id, day)).Should().Be(80m);
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, day)).Should().Be(350m);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(350m, "the day's end is still the stated balance");
        (await Client.ListOperationsAsync(setup.Wallet.Id)).Where(x => x.OperationDate == day).Last().IsCorrection.Should().BeTrue();
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    [Fact]
    public async Task SeveralStatedBalances_EachAbsorbsOnlyTheDiscrepancyAfterThePreviousOne()
    {
        var setup = await CreateWalletAsync();
        var tenth = Today.AddDays(-30);
        var twentieth = Today.AddDays(-20);

        await Client.SetStatedBalanceAsync(setup.Wallet.Id, twentieth, 100m);
        (await CorrectionOnAsync(setup.Wallet.Id, twentieth)).Should().Be(100m);

        // A statement for an earlier date: the 20th now only absorbs what is left after it.
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, tenth, 100m);

        (await CorrectionOnAsync(setup.Wallet.Id, tenth)).Should().Be(100m);
        (await CorrectionOnAsync(setup.Wallet.Id, twentieth)).Should().Be(0m);
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, tenth.AddDays(-1))).Should().Be(0m, "a statement does not change the balance before its date");
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, tenth)).Should().Be(100m);
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, twentieth)).Should().Be(100m);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(100m);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id);
    }

    // ---- Soft delete / restore of an operation before a stated date ----

    [Fact]
    public async Task DeleteAndRestoreOfAnOperationBeforeAStatedDate_RecalculatesTheCorrectionAndKeepsTheBalance()
    {
        var setup = await CreateWalletAsync();
        var first = Today.AddDays(-20);
        var tenth = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 500m, first);
        var expenseId = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.ExpenseTypeId, 200m, tenth);
        var (_, correction) = await Client.SetStatedBalanceAsync(setup.Wallet.Id, tenth, 350m);
        (await CorrectionOnAsync(setup.Wallet.Id, tenth)).Should().Be(50m);

        (await Client.DeleteAsync($"/api/v1/operations/{expenseId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await CorrectionOnAsync(setup.Wallet.Id, tenth)).Should().Be(-150m, "the expense is gone, so 350 stated vs 500");
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, tenth)).Should().Be(350m);
        (await SnapshotAsync(setup.Wallet.Id, tenth)).Should().Be(350m);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(350m, "the stated balance keeps the current balance");
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id, "after the deletion");

        (await Client.PostAsync($"/api/v1/operations/{expenseId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await CorrectionOnAsync(setup.Wallet.Id, tenth)).Should().Be(50m);
        (await Client.GetWalletBalanceOnDateAsync(setup.Wallet.Id, tenth)).Should().Be(350m);
        (await Client.GetWalletBalanceAsync(setup.Wallet.Id)).Should().Be(350m);
        await Fixture.AssertWalletInvariantAsync(setup.Wallet.Id, "after the restoration");
        (await Client.GetAuditEntriesAsync("Operation", correction.Id)).Select(e => e.Action)
            .Should().BeEquivalentTo("Created", "Recalculated", "Recalculated");
    }

    // ---- Move to another wallet ----

    [Fact]
    public async Task MovingAnOperationBetweenWalletsWithStatedBalances_RecalculatesBothWallets()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("MB" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletA = await Client.CreateWalletAsync(0m, Today.AddDays(-30), "A", currencyId: currency.Id);
        var walletB = await Client.CreateWalletAsync(0m, Today.AddDays(-30), "B", currencyId: currency.Id);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var opDate = Today.AddDays(-10);
        var stated = Today.AddDays(-5);
        var operationId = await Client.CreateOperationReturningIdAsync(walletA.Id, incomeTypeId, 100m, opDate);
        await Client.SetStatedBalanceAsync(walletA.Id, stated, 100m); // zero correction
        await Client.SetStatedBalanceAsync(walletB.Id, stated, 40m);  // +40

        var response = await PatchAsync(operationId, walletB.Id, incomeTypeId, 100m, opDate);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await CorrectionOnAsync(walletA.Id, stated)).Should().Be(100m, "A lost the income, the statement absorbs it");
        (await CorrectionOnAsync(walletB.Id, stated)).Should().Be(-60m, "B gained the income: 40 stated vs 100");
        foreach (var wallet in new[] { walletA, walletB })
        {
            await Fixture.AssertWalletInvariantAsync(wallet.Id, "after the move");
        }

        (await Client.GetWalletBalanceOnDateAsync(walletA.Id, stated)).Should().Be(100m);
        (await Client.GetWalletBalanceOnDateAsync(walletB.Id, stated)).Should().Be(40m);
        (await Client.GetWalletBalanceAsync(walletA.Id)).Should().Be(100m);
        (await Client.GetWalletBalanceAsync(walletB.Id)).Should().Be(40m);
        (await SnapshotAsync(walletA.Id, stated)).Should().Be(100m);
        (await SnapshotAsync(walletB.Id, stated)).Should().Be(40m);
        (await Client.GetAuditEntriesAsync("Operation", (await Client.ListedCorrectionAsync(walletA.Id, stated))!.Id))
            .Select(e => e.Action).Should().Contain("Recalculated");
    }

    // ---- Transfers ----

    [Fact]
    public async Task TransferCreateDeleteRestore_RecalculatesCorrectionsOfBothWallets()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("TR" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var source = await Client.CreateWalletAsync(1000m, Today.AddDays(-30), "Источник", walletType.Id, currency.Id);
        var target = await Client.CreateWalletAsync(0m, Today.AddDays(-30), "Назначение", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        var stated = Today.AddDays(-5);
        await Client.SetStatedBalanceAsync(source.Id, stated, 1000m); // zero
        await Client.SetStatedBalanceAsync(target.Id, stated, 0m);    // zero

        var created = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id, TargetWalletId = target.Id, Amount = 150m, TransferDate = Today.AddDays(-10),
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var transferId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Both statements absorb the transfer: +150 on the source, -150 on the target; current balances stay as stated.
        await AssertTransferStateAsync(source.Id, target.Id, stated, sourceCorrection: 150m, targetCorrection: -150m, "after the creation");

        (await Client.DeleteAsync($"/api/v1/transfers/{transferId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        await AssertTransferStateAsync(source.Id, target.Id, stated, sourceCorrection: 0m, targetCorrection: 0m, "after the deletion");

        (await Client.PostAsync($"/api/v1/transfers/{transferId}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertTransferStateAsync(source.Id, target.Id, stated, sourceCorrection: 150m, targetCorrection: -150m, "after the restoration");
    }

    private async Task AssertTransferStateAsync(
        Guid sourceId, Guid targetId, DateOnly stated, decimal sourceCorrection, decimal targetCorrection, string step)
    {
        (await CorrectionOnAsync(sourceId, stated)).Should().Be(sourceCorrection, step);
        (await CorrectionOnAsync(targetId, stated)).Should().Be(targetCorrection, step);
        (await Client.GetWalletBalanceOnDateAsync(sourceId, stated)).Should().Be(1000m, step);
        (await Client.GetWalletBalanceOnDateAsync(targetId, stated)).Should().Be(0m, step);
        (await Client.GetWalletBalanceAsync(sourceId)).Should().Be(1000m, step);
        (await Client.GetWalletBalanceAsync(targetId)).Should().Be(0m, step);
        await Fixture.AssertWalletInvariantAsync(sourceId, step);
        await Fixture.AssertWalletInvariantAsync(targetId, step);
        (await SnapshotAsync(sourceId, stated)).Should().Be(1000m, step);
        (await SnapshotAsync(targetId, stated)).Should().Be(0m, step);
    }

    // ---- List order, pagination, filters ----

    [Fact]
    public async Task List_CorrectionStaysLastInItsDayEvenWhenAnOperationOfThatDayIsAddedLater()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 500m, day);
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 300m);
        var late = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.ExpenseTypeId, 50m, day);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 1m, day.AddDays(1));

        var listed = await Client.ListOperationsAsync(setup.Wallet.Id);

        var ofDay = listed.Where(x => x.OperationDate == day).ToList();
        ofDay.Should().HaveCount(3);
        ofDay.Last().IsCorrection.Should().BeTrue("the correction follows every regular row of its day, whatever the entry order");
        ofDay.Select(x => x.Id).Should().Contain(late);
        listed.Last().OperationDate.Should().Be(day.AddDays(1));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task List_CursorPagination_ReturnsEveryRowOnceInTheSameOrderAsTheFullList(int limit)
    {
        var setup = await CreateWalletAsync();
        var d1 = Today.AddDays(-20);
        var d2 = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 500m, d1);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.ExpenseTypeId, 100m, d1);
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, d1, 300m);                  // -100 on d1
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 10m, d2);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 20m, d2);
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, d2, 400m);                  // +70 on d2
        var full = await Client.ListOperationsAsync(setup.Wallet.Id, limit: 100);

        var paged = await Client.ListOperationsAsync(setup.Wallet.Id, limit);

        full.Should().HaveCount(6);
        paged.Select(x => x.Id).Should().Equal(full.Select(x => x.Id), "no gaps, no duplicates, same order");
        paged.Select(x => x.IsCorrection).Should().Equal(new[] { false, false, true, false, false, true });
    }

    [Fact]
    public async Task List_CursorIssuedBeforeCorrectionsExisted_StillDecodesAndContinuesAfterThatRow()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        var firstId = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 100m, day);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 10m, day.AddDays(1));
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 20m, day.AddDays(2));
        var first = await GetOperationAsync(firstId);
        // The former format of the cursor: "<date>|<createdAt>" without the correction flag.
        var oldCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{first.OperationDate:O}|{first.CreatedAt:O}"));

        var response = await Client.GetAsync(
            $"/api/v1/operations?walletId={setup.Wallet.Id}&limit=100&cursor={Uri.EscapeDataString(oldCursor)}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data").EnumerateArray().ToList();
        rows.Should().HaveCount(2, "everything after the first operation");
        rows.Select(x => x.GetProperty("id").GetGuid()).Should().NotContain(firstId);
    }

    [Fact]
    public async Task List_FilterByOperationType_DoesNotIncludeCorrections_ButDateAndWalletFiltersDo()
    {
        var setup = await CreateWalletAsync();
        var day = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 100m, day);
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, day, 60m);

        var byType = (await Client.ListOperationsAsync(setup.Wallet.Id, extraQuery: $"&operationTypeId={setup.IncomeTypeId}"));
        var byDate = (await Client.ListOperationsAsync(setup.Wallet.Id, extraQuery: $"&dateFrom={day:yyyy-MM-dd}&dateTo={day:yyyy-MM-dd}"));

        byType.Should().NotContain(x => x.IsCorrection);
        byType.Should().ContainSingle();
        byDate.Select(x => x.IsCorrection).Should().Equal(new[] { false, true });
    }

    // ---- Wallet currency and lookups ----

    [Fact]
    public async Task OperationsOfACorrection_AreNotCountedAsWalletHistoryOrReferenceUsage()
    {
        var setup = await CreateWalletAsync();
        await Client.SetStatedBalanceAsync(setup.Wallet.Id, Today.AddDays(-5), 100m);

        // A wallet with only a stated balance: no history, so it can be deleted (see also the wallet tests) ...
        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IWalletHistorySource>();
            (await history.HasHistoryAsync(new WalletId(setup.Wallet.Id), CancellationToken.None))
                .Should().BeFalse("a correction is not a fact entered by the user");

            // ... and an operation type or a currency is not used by a correction.
            var probes = scope.ServiceProvider.GetServices<IReferenceItemUsageProbe>();
            var operationsProbe = probes.Single(p => p.GetType().Name == "OperationsReferenceItemUsageProbe");
            (await operationsProbe.IsUsedAsync(ReferenceItemKind.Currency, setup.Wallet.CurrencyId, CancellationToken.None))
                .Should().BeFalse();
            (await operationsProbe.IsUsedAsync(ReferenceItemKind.OperationType, setup.IncomeTypeId, CancellationToken.None))
                .Should().BeFalse();
        }

        // The first regular operation makes both true.
        await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 10m, Today.AddDays(-4));
        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            var history = scope.ServiceProvider.GetRequiredService<IWalletHistorySource>();
            (await history.HasHistoryAsync(new WalletId(setup.Wallet.Id), CancellationToken.None)).Should().BeTrue();
        }
    }

    [Fact]
    public async Task RegularOperations_ReportNoCorrectionFlagAndNoStatedBalance()
    {
        var setup = await CreateWalletAsync();
        var id = await Client.CreateOperationReturningIdAsync(setup.Wallet.Id, setup.IncomeTypeId, 10m, Today.AddDays(-1));

        var operation = await GetOperationAsync(id);

        operation.IsCorrection.Should().BeFalse();
        operation.StatedBalance.Should().BeNull();
        operation.OperationTypeId.Should().Be(setup.IncomeTypeId);
    }
}
