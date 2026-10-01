using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// The startup pass of StatedBalanceReconciliationHostedService (ADR-0003, §6; ADR-0001, §6) against a real database.
/// The test host removes every hosted service, so the pass is run directly. It must heal stale corrections and wallet
/// balances of all wallets that have corrections, leave other wallets alone, and survive a failing wallet.
/// </summary>
public sealed class StatedBalanceReconciliationHostedServiceTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => RecordLifecycleTestHelpers.Today;

    private StatedBalanceReconciliationHostedService CreateService() => new(
        Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(), NullLogger<StatedBalanceReconciliationHostedService>.Instance);

    /// <summary>A wallet with +300 on day -10 and 350 stated on day -5 (correction +50), then made stale like an older format would leave it.</summary>
    private async Task<(Guid WalletId, Guid CorrectionId)> CreateStaleWalletAsync(string name)
    {
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20), name: name);
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income", $"Доход {name}");
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-10));
        var (_, correction) = await Client.SetStatedBalanceAsync(wallet.Id, Today.AddDays(-5), 350m);
        await using var connection = await Fixture.OpenConnectionAsync();
        await connection.ExecuteAsync(
            $"UPDATE operations.operations SET amount = 7, applied_delta_amount = 7 WHERE id = '{correction.Id}'");
        await connection.ExecuteAsync($"UPDATE wallets.wallets SET current_balance_amount = 1 WHERE id = '{wallet.Id}'");
        return (wallet.Id, correction.Id);
    }

    [Fact]
    public async Task RunPass_HealsStaleCorrectionsAndCurrentBalanceOfEveryWalletWithCorrections()
    {
        await AuthenticateAsync();
        var (firstWallet, _) = await CreateStaleWalletAsync("Первый");
        var (secondWallet, _) = await CreateStaleWalletAsync("Второй");

        await CreateService().RunPassAsync(CancellationToken.None);

        foreach (var walletId in new[] { firstWallet, secondWallet })
        {
            (await Fixture.CorrectionAmountAsync(walletId, Today.AddDays(-5))).Should().Be(50m);
            (await Client.GetWalletBalanceAsync(walletId)).Should().Be(350m, "current_balance_amount is back at the invariant");
            await Fixture.AssertWalletInvariantAsync(walletId, "after the startup pass");
            (await Client.GetWalletBalanceOnDateAsync(walletId, Today.AddDays(-5))).Should().Be(350m);
        }
    }

    [Fact]
    public async Task RunPass_RecordsTheRecalculationUnderTheSystemActorWithTheServiceName()
    {
        await AuthenticateAsync();
        var (_, correctionId) = await CreateStaleWalletAsync("Первый");

        await CreateService().RunPassAsync(CancellationToken.None);

        var recalculated = (await Client.GetAuditEntriesAsync("Operation", correctionId)).Single(e => e.Action == "Recalculated");
        recalculated.ActorKind.Should().Be("System");
        recalculated.ActorSystemProcess.Should().Be(nameof(StatedBalanceReconciliationHostedService));
    }

    [Fact]
    public async Task RunPass_ASecondPassChangesNothing()
    {
        await AuthenticateAsync();
        await CreateStaleWalletAsync("Первый");
        await CreateService().RunPassAsync(CancellationToken.None);
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await connection.CountAsync("SELECT count(*) FROM audit.audit_entries");

        await CreateService().RunPassAsync(CancellationToken.None);

        (await connection.CountAsync("SELECT count(*) FROM audit.audit_entries")).Should().Be(auditBefore);
    }

    [Fact]
    public async Task RunPass_LeavesWalletsWithoutCorrectionsAlone()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-10));
        await using var connection = await Fixture.OpenConnectionAsync();
        await connection.ExecuteAsync($"UPDATE wallets.wallets SET current_balance_amount = 5 WHERE id = '{wallet.Id}'");

        await CreateService().RunPassAsync(CancellationToken.None);

        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(5m, "the pass covers only wallets that have corrections");
    }

    [Fact]
    public async Task RunPass_AFailingWalletIsLoggedAndDoesNotStopTheOthers()
    {
        await AuthenticateAsync();
        var first = await CreateStaleWalletAsync("Первый");
        var second = await CreateStaleWalletAsync("Второй");

        // The pass takes the wallets in the order the repository returns them: the one it meets FIRST fails, so that a pass
        // that gave up after the first error would leave the second wallet unhealed.
        List<Guid> passOrder;
        using (var scope = Fixture.Factory.Services.CreateScope())
        {
            passOrder = (await scope.ServiceProvider.GetRequiredService<IOperationRepository>()
                .GetWalletIdsWithCorrectionsAsync(CancellationToken.None)).Select(x => x.Value).ToList();
        }

        var (failingWallet, failingCorrection) = passOrder[0] == first.WalletId ? first : second;
        var healthyWallet = passOrder[1] == first.WalletId ? first.WalletId : second.WalletId;
        await using var connection = await Fixture.OpenConnectionAsync();
        await connection.ExecuteAsync(
            "CREATE FUNCTION audit.fail_test_insert() RETURNS trigger AS $$ " +
            "BEGIN RAISE EXCEPTION 'test-induced audit failure'; END; $$ LANGUAGE plpgsql");
        await connection.ExecuteAsync(
            "CREATE TRIGGER fail_test_insert BEFORE INSERT ON audit.audit_entries FOR EACH ROW " +
            $"WHEN (NEW.entity_id = '{failingCorrection}' AND NEW.action = 'Recalculated') EXECUTE FUNCTION audit.fail_test_insert()");
        try
        {
            var act = () => CreateService().RunPassAsync(CancellationToken.None);

            await act.Should().NotThrowAsync("an error of one wallet is logged, never propagated to the host");
        }
        finally
        {
            await connection.ExecuteAsync("DROP TRIGGER IF EXISTS fail_test_insert ON audit.audit_entries");
            await connection.ExecuteAsync("DROP FUNCTION IF EXISTS audit.fail_test_insert()");
        }

        (await Fixture.CorrectionAmountAsync(healthyWallet, Today.AddDays(-5))).Should().Be(50m, "the other wallet was reconciled");
        (await Client.GetWalletBalanceAsync(healthyWallet)).Should().Be(350m);
        (await Fixture.CorrectionAmountAsync(failingWallet, Today.AddDays(-5))).Should().Be(7m, "the failing wallet was rolled back as a whole");
        (await Client.GetWalletBalanceAsync(failingWallet)).Should().Be(1m);
    }
}
