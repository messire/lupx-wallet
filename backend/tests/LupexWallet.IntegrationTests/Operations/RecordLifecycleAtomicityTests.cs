using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Atomicity of the soft delete / restore commands (ADR-0001, §4/§6; ADR-0002, §3; ADR-0006): the
/// operation/transfer rows, the wallets, the recalculated snapshots and the audit entries written by one
/// command are committed by ONE PostgreSQL transaction (the same xmin), and a failure anywhere inside
/// (here: the audit insert) rolls the whole command back. Technique as in BalanceSnapshotSchedulerTests.
/// </summary>
public sealed class RecordLifecycleAtomicityTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private async Task<(Guid WalletId, Guid OperationId)> CreateWalletWithPastOperationAsync()
    {
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        return (wallet.Id, operationId);
    }

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

    /// <summary>xmin of every audit entry that appeared after <paramref name="idsBefore"/>, with its entity type and action.</summary>
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
    public async Task DeleteOperation_WritesOperationWalletSnapshotsAndAuditEntriesInOneTransaction()
    {
        await AuthenticateAsync();
        var (walletId, operationId) = await CreateWalletWithPastOperationAsync();
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await ReadAuditIdsAsync(connection);

        var response = await Client.DeleteAsync($"/api/v1/operations/{operationId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var newAudit = await ReadNewAuditEntriesAsync(connection, auditBefore);
        newAudit.Select(e => (e.EntityType, e.Action)).Should().Contain(("Operation", "Deleted"))
            .And.Contain(("Wallet", "BalanceChanged"))
            .And.Contain(("BalanceSnapshot", "SnapshotUpdated"));

        var xmins = new List<string>();
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM operations.operations WHERE id = @id", ("id", operationId)));
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", walletId)));
        var snapshotXmins = await connection.ReadXminsAsync(
            "SELECT xmin::text FROM balance_history.balance_snapshots WHERE wallet_id = @id", ("id", walletId));
        snapshotXmins.Should().HaveCount(4, "snapshots from the operation date through today were recalculated");
        xmins.AddRange(snapshotXmins);
        xmins.AddRange(newAudit.Select(e => e.Xmin));

        xmins.Distinct().Should().ContainSingle(
            "operation, wallet, snapshots and audit entries of one DELETE are written by a single physical transaction");
    }

    [Fact]
    public async Task RestoreOperation_WritesOperationWalletSnapshotsAndAuditEntriesInOneTransaction()
    {
        await AuthenticateAsync();
        var (walletId, operationId) = await CreateWalletWithPastOperationAsync();
        await Client.DeleteAsync($"/api/v1/operations/{operationId}");
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await ReadAuditIdsAsync(connection);

        var response = await Client.PostAsync($"/api/v1/operations/{operationId}/restore", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var newAudit = await ReadNewAuditEntriesAsync(connection, auditBefore);
        newAudit.Select(e => (e.EntityType, e.Action)).Should().Contain(("Operation", "Restored"))
            .And.Contain(("Wallet", "BalanceChanged"))
            .And.Contain(("BalanceSnapshot", "SnapshotUpdated"));

        var xmins = new List<string>();
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM operations.operations WHERE id = @id", ("id", operationId)));
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM wallets.wallets WHERE id = @id", ("id", walletId)));
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM balance_history.balance_snapshots WHERE wallet_id = @id", ("id", walletId)));
        xmins.AddRange(newAudit.Select(e => e.Xmin));

        xmins.Distinct().Should().ContainSingle(
            "operation, wallet, snapshots and audit entries of one restore are written by a single physical transaction");
    }

    [Fact]
    public async Task DeleteTransfer_WritesTransferBothLegsBothWalletsSnapshotsAndAuditInOneTransaction()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("AT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var source = await Client.CreateWalletAsync(500m, Today.AddDays(-10), "Источник", walletType.Id, currency.Id);
        var target = await Client.CreateWalletAsync(100m, Today.AddDays(-10), "Назначение", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        var transferResponse = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = Today.AddDays(-3),
        });
        transferResponse.EnsureSuccessStatusCode();
        var body = await transferResponse.Content.ReadFromJsonAsync<JsonElement>();
        var transferId = body.GetProperty("id").GetGuid();
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditBefore = await ReadAuditIdsAsync(connection);

        var response = await Client.DeleteAsync($"/api/v1/transfers/{transferId}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var newAudit = await ReadNewAuditEntriesAsync(connection, auditBefore);
        newAudit.Count(e => e is { EntityType: "Operation", Action: "Deleted" }).Should().Be(2);
        newAudit.Should().Contain(e => e.EntityType == "Transfer" && e.Action == "Deleted");

        var xmins = new List<string>();
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM operations.transfers WHERE id = @id", ("id", transferId)));
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM operations.operations WHERE transfer_id = @id", ("id", transferId)));
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM wallets.wallets WHERE id = @source OR id = @target",
            ("source", source.Id), ("target", target.Id)));
        xmins.AddRange(await connection.ReadXminsAsync(
            "SELECT xmin::text FROM balance_history.balance_snapshots WHERE wallet_id = @source OR wallet_id = @target",
            ("source", source.Id), ("target", target.Id)));
        xmins.AddRange(newAudit.Select(e => e.Xmin));

        xmins.Distinct().Should().ContainSingle(
            "transfer, both legs, both wallets, all snapshots and all audit entries of one transfer DELETE share a transaction");
    }

    [Fact]
    public async Task DeleteOperation_WhenAuditInsertFails_RollsBackOperationWalletAndSnapshots()
    {
        await AuthenticateAsync();
        var (walletId, operationId) = await CreateWalletWithPastOperationAsync();
        var snapshotsBefore = (await Client.GetSnapshotsAsync(walletId, Today.AddDays(-10), Today))
            .ToDictionary(s => s.Date, s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture));
        snapshotsBefore[Today].Should().Be(1300m);
        await using var connection = await Fixture.OpenConnectionAsync();
        var auditCountBefore = await connection.CountAsync("SELECT count(*) FROM audit.audit_entries");

        await connection.ExecuteAsync(
            "CREATE FUNCTION audit.fail_test_insert() RETURNS trigger AS $$ " +
            "BEGIN RAISE EXCEPTION 'test-induced audit failure'; END; $$ LANGUAGE plpgsql");
        await connection.ExecuteAsync(
            "CREATE TRIGGER fail_test_insert BEFORE INSERT ON audit.audit_entries " +
            "FOR EACH ROW WHEN (NEW.entity_type = 'Operation' AND NEW.action = 'Deleted') EXECUTE FUNCTION audit.fail_test_insert()");
        HttpStatusCode status;
        try
        {
            status = (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode;
        }
        finally
        {
            await connection.ExecuteAsync("DROP TRIGGER IF EXISTS fail_test_insert ON audit.audit_entries");
            await connection.ExecuteAsync("DROP FUNCTION IF EXISTS audit.fail_test_insert()");
        }

        status.Should().Be(HttpStatusCode.InternalServerError);
        var operation = (await (await Client.GetAsync($"/api/v1/operations/{operationId}")).Content.ReadFromJsonAsync<OperationResponse>())!;
        operation.IsDeleted.Should().BeFalse("the failed audit insert rolled the soft deletion back");
        (await Client.GetWalletBalanceAsync(walletId)).Should().Be(1300m, "the reversed delta was rolled back with the command");
        (await Client.GetSnapshotsAsync(walletId, Today.AddDays(-10), Today))
            .ToDictionary(s => s.Date, s => decimal.Parse(s.Balance.Amount, CultureInfo.InvariantCulture))
            .Should().BeEquivalentTo(snapshotsBefore, "snapshot recalculation was rolled back too");
        (await connection.CountAsync("SELECT count(*) FROM audit.audit_entries")).Should().Be(
            auditCountBefore, "no partial audit trail is left by the failed command");
    }
}
