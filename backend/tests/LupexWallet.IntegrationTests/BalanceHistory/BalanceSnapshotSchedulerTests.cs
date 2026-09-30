using FluentAssertions;
using LupexWallet.BalanceHistory.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.BalanceHistory;

/// <summary>
/// Плановый проход слепков баланса (ADR-0003, §3/§4) идёт через команду и TransactionBehavior
/// (ADR-0001, §4/§6): слепки кошелька и их AuditEntry записываются в одной транзакции, каждый
/// кошелёк — в собственном DI-scope. Фоновые сервисы в тестовом Host отключены
/// (LupexWalletApiFactory), поэтому проход запускается вручную через
/// <see cref="BalanceSnapshotSchedulerHostedService.RunPassAsync"/>.
/// </summary>
public sealed class BalanceSnapshotSchedulerTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private const string AuditActor = "BalanceSnapshotSchedulerHostedService";

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task RunPass_WritesSnapshotsAndTheirAuditEntriesInTheSameTransaction()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-3);
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 500m, accountingStartDate: accountingStartDate);

        await CreateScheduler().RunPassAsync(CancellationToken.None);

        await using var connection = await OpenConnectionAsync();
        var snapshotTransactions = await ReadSnapshotTransactionsAsync(connection, wallet.Id);
        var auditTransactions = await ReadAuditTransactionsAsync(connection, wallet.Id);

        snapshotTransactions.Should().HaveCount(4, "слепки [accountingStartDate..сегодня] включительно");
        auditTransactions.Should().HaveCount(4, "по одной AuditEntry на слепок, актор — System (планировщик)");
        snapshotTransactions.Concat(auditTransactions).Distinct().Should().ContainSingle(
            "слепки и их AuditEntry записаны одной физической транзакцией PostgreSQL (один xmin)");
    }

    [Fact]
    public async Task RunPass_WalletFailsOnSave_NextWalletStillGetsSnapshotsWithAuditInOneTransaction()
    {
        await AuthenticateAsync();
        var accountingStartDate = Today.AddDays(-3);
        await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: accountingStartDate, name: "Первый");
        await Client.CreateWalletAsync(initialBalanceAmount: 200m, accountingStartDate: accountingStartDate, name: "Второй");

        // The pass processes wallets in the order IWalletDirectory returns them (unordered query),
        // so the wallet to fail is taken from that same order: it must be processed first.
        var walletIds = await ReadWalletIdsInPassOrderAsync();
        walletIds.Should().HaveCount(2);
        var failingWalletId = walletIds[0];
        var healthyWalletId = walletIds[1];

        await using var connection = await OpenConnectionAsync();
        await CreateFailingInsertTriggerAsync(connection, failingWalletId);
        try
        {
            await CreateScheduler().RunPassAsync(CancellationToken.None);
        }
        finally
        {
            await DropFailingInsertTriggerAsync(connection);
        }

        var failedSnapshots = await ReadSnapshotTransactionsAsync(connection, failingWalletId);
        var failedAudit = await ReadAuditTransactionsAsync(connection, failingWalletId);
        var healthySnapshots = await ReadSnapshotTransactionsAsync(connection, healthyWalletId);
        var healthyAudit = await ReadAuditTransactionsAsync(connection, healthyWalletId);

        failedSnapshots.Should().BeEmpty("the failed wallet's transaction was rolled back");
        failedAudit.Should().BeEmpty();
        healthySnapshots.Should().HaveCount(4, "слепки [accountingStartDate..сегодня] включительно");
        healthyAudit.Should().HaveCount(4, "по одной AuditEntry на слепок второго кошелька");
        healthySnapshots.Concat(healthyAudit).Distinct().Should().ContainSingle(
            "слепки второго кошелька и их AuditEntry — одна транзакция, без примеси слепков упавшего кошелька");
    }

    private BalanceSnapshotSchedulerHostedService CreateScheduler() => new(
        Fixture.Factory.Services.GetRequiredService<IServiceScopeFactory>(),
        NullLogger<BalanceSnapshotSchedulerHostedService>.Instance);

    private async Task<List<Guid>> ReadWalletIdsInPassOrderAsync()
    {
        await using var scope = Fixture.Factory.Services.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<IWalletDirectory>()
            .GetAllWalletIdsAsync(CancellationToken.None);
        return ids.Select(id => id.Value).ToList();
    }

    private async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        var connection = new NpgsqlConnection(Fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    /// <summary>
    /// Makes the INSERT of the given wallet's snapshots fail inside PostgreSQL, i.e. a real
    /// SaveChangesAsync failure after the snapshots were added to the change tracker.
    /// </summary>
    private static async Task CreateFailingInsertTriggerAsync(NpgsqlConnection connection, Guid walletId)
    {
        await ExecuteAsync(
            connection,
            "CREATE FUNCTION balance_history.fail_test_insert() RETURNS trigger AS $$ " +
            "BEGIN RAISE EXCEPTION 'test-induced insert failure'; END; $$ LANGUAGE plpgsql");
        await ExecuteAsync(
            connection,
            "CREATE TRIGGER fail_test_insert BEFORE INSERT ON balance_history.balance_snapshots " +
            $"FOR EACH ROW WHEN (NEW.wallet_id = '{walletId}') EXECUTE FUNCTION balance_history.fail_test_insert()");
    }

    private static async Task DropFailingInsertTriggerAsync(NpgsqlConnection connection)
    {
        await ExecuteAsync(connection, "DROP TRIGGER IF EXISTS fail_test_insert ON balance_history.balance_snapshots");
        await ExecuteAsync(connection, "DROP FUNCTION IF EXISTS balance_history.fail_test_insert()");
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static Task<List<string>> ReadSnapshotTransactionsAsync(NpgsqlConnection connection, Guid walletId) =>
        ReadTransactionIdsAsync(
            connection,
            "SELECT xmin::text FROM balance_history.balance_snapshots WHERE wallet_id = @walletId",
            walletId);

    private static Task<List<string>> ReadAuditTransactionsAsync(NpgsqlConnection connection, Guid walletId) =>
        ReadTransactionIdsAsync(
            connection,
            "SELECT xmin::text FROM audit.audit_entries " +
            $"WHERE entity_type = 'BalanceSnapshot' AND entity_id = @walletId AND actor_system_process = '{AuditActor}'",
            walletId);

    private static async Task<List<string>> ReadTransactionIdsAsync(NpgsqlConnection connection, string sql, Guid walletId)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("walletId", walletId);

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
