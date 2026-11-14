using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Atomicity of the stated-balance commands (ADR-0001, §4; ADR-0003, §6; ADR-0006): the correction row, the wallet,
/// the recalculated snapshots and the audit entries of ONE command are written by one physical transaction (the same
/// xmin), and a failure anywhere inside (here: the audit insert) rolls everything back, the correction included.
/// </summary>
public sealed class StatedBalanceAtomicityTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => RecordLifecycleTestHelpers.Today;

    private static async Task<HashSet<Guid>> ReadAuditIdsAsync(NpgsqlConnection connection)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id FROM audit.audit_entries";
        var ids = new HashSet<Guid>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            ids.Add(reader.GetGuid(0));
        }

        return ids;
    }

    private static async Task<List<(string EntityType, string Action, string Xmin)>> ReadNewAuditEntriesAsync(
        NpgsqlConnection connection, HashSet<Guid> idsBefore)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, entity_type, action, xmin::text FROM audit.audit_entries";
        var result = new List<(string, string, string)>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (!idsBefore.Contains(reader.GetGuid(0)))
            {
                result.Add((reader.GetString(1), reader.GetString(2), reader.GetString(3)));
            }
        }

        return result;
    }

    [Fact]
    public async Task Put_WritesCorrectionWalletSnapshotsAndAuditEntriesInOneTransaction()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var statedDate = Today.AddDays(-5);
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-10));
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await ReadAuditIdsAsync(connection);

        var (status, correction) = await Client.SetStatedBalanceAsync(wallet.Id, statedDate, 350m);

        status.Should().Be(HttpStatusCode.Created);
        correction.Amount.Amount.Should().Be("50");
        var newAudit = await ReadNewAuditEntriesAsync(connection, auditBefore);
        newAudit.Select(e => (e.EntityType, e.Action)).Should().Contain(("Operation", "Created"))
            .And.Contain(("Wallet", "BalanceChanged"))
            .And.Contain(("BalanceSnapshot", "SnapshotUpdated"));

        var xmins = new List<string>();
        xmins.AddRange(await connection.ReadXminsAsync("SELECT xmin::text FROM operations.operations WHERE id = @id", ("id", correction.Id)));
        xmins.AddRange(await connection.ReadXminsAsync("SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", wallet.Id)));
        var snapshotXmins = await connection.ReadXminsAsync(
            "SELECT xmin::text FROM balance_history.balance_snapshots WHERE wallet_id = @id AND snapshot_date >= @from",
            ("id", wallet.Id), ("from", statedDate));
        snapshotXmins.Should().HaveCount(6, "snapshots from the stated date through today were recalculated");
        xmins.AddRange(snapshotXmins);
        xmins.AddRange(newAudit.Select(e => e.Xmin));

        xmins.Distinct().Should().ContainSingle("the correction, the wallet, the snapshots and the audit entries of one PUT share a transaction");
    }

    [Fact]
    public async Task PostOperationRecalculatingACorrection_WritesBothOperationsSnapshotsAndAuditEntriesInOneTransaction()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var statedDate = Today.AddDays(-5);
        var operationDate = Today.AddDays(-10);
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-15));
        var (_, correction) = await Client.SetStatedBalanceAsync(wallet.Id, statedDate, 350m);
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await ReadAuditIdsAsync(connection);
        var walletXminBefore = await connection.ReadXminsAsync("SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", wallet.Id));

        // +100 before the stated date: the correction (+50) drops to -50, the balance on the stated date stays 350.
        var newId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 100m, operationDate);

        (await Fixture.CorrectionAmountAsync(wallet.Id, statedDate)).Should().Be(-50m);
        var newAudit = await ReadNewAuditEntriesAsync(connection, auditBefore);
        newAudit.Select(e => (e.EntityType, e.Action)).Should().Contain(("Operation", "Created"))
            .And.Contain(("Operation", "Recalculated"))
            .And.Contain(("BalanceSnapshot", "SnapshotUpdated"));

        var xmins = new List<string>();
        xmins.AddRange(await connection.ReadXminsAsync("SELECT xmin::text FROM operations.operations WHERE id = @id", ("id", newId)));
        xmins.AddRange(await connection.ReadXminsAsync("SELECT xmin::text FROM operations.operations WHERE id = @id", ("id", correction.Id)));
        var snapshotXmins = await connection.ReadXminsAsync(
            "SELECT xmin::text FROM balance_history.balance_snapshots WHERE wallet_id = @id AND snapshot_date >= @from AND snapshot_date < @to",
            ("id", wallet.Id), ("from", operationDate), ("to", statedDate));
        snapshotXmins.Should().HaveCount(5, "the snapshots between the operation and the stated date changed");
        xmins.AddRange(snapshotXmins);
        xmins.AddRange(newAudit.Select(e => e.Xmin));

        xmins.Distinct().Should().ContainSingle("the operation, the recalculated correction, the snapshots and the audit entries share a transaction");
        (await connection.ReadXminsAsync("SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", wallet.Id)))
            .Should().Equal(walletXminBefore, "the later stated balance absorbed the +100: CurrentBalance did not change, the wallet row was not written");
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(350m);
    }

    [Fact]
    public async Task Put_WhenAuditInsertFails_RollsBackTheCorrectionTheWalletAndTheSnapshots()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-10));
        var snapshotsBefore = (await Client.GetSnapshotsAsync(wallet.Id, Today.AddDays(-20), Today))
            .ToDictionary(s => s.Date, s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture));
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditCountBefore = await connection.CountAsync("SELECT count(*) FROM audit.audit_entries");

        await connection.ExecuteAsync(
            "CREATE FUNCTION audit.fail_test_insert() RETURNS trigger AS $$ " +
            "BEGIN RAISE EXCEPTION 'test-induced audit failure'; END; $$ LANGUAGE plpgsql");
        await connection.ExecuteAsync(
            "CREATE TRIGGER fail_test_insert BEFORE INSERT ON audit.audit_entries " +
            "FOR EACH ROW WHEN (NEW.entity_type = 'Operation' AND NEW.action = 'Created') EXECUTE FUNCTION audit.fail_test_insert()");
        HttpStatusCode status;
        try
        {
            status = (await Client.PutAsJsonAsync(
                $"/api/v1/wallets/{wallet.Id}/stated-balances/{Today.AddDays(-5):yyyy-MM-dd}", new { Amount = 350m })).StatusCode;
        }
        finally
        {
            await connection.ExecuteAsync("DROP TRIGGER IF EXISTS fail_test_insert ON audit.audit_entries");
            await connection.ExecuteAsync("DROP FUNCTION IF EXISTS audit.fail_test_insert()");
        }

        status.Should().Be(HttpStatusCode.InternalServerError);
        (await Fixture.CorrectionRowCountAsync(wallet.Id)).Should().Be(0, "the correction row was rolled back");
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(300m, "the balance change was rolled back");
        (await Client.GetSnapshotsAsync(wallet.Id, Today.AddDays(-20), Today))
            .ToDictionary(s => s.Date, s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture))
            .Should().BeEquivalentTo(snapshotsBefore, "the snapshot recalculation was rolled back too");
        (await connection.CountAsync("SELECT count(*) FROM audit.audit_entries")).Should().Be(auditCountBefore);
    }
}
