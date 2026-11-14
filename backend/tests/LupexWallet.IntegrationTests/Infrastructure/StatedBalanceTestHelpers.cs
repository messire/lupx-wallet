using System.Globalization;
using System.Net.Http.Json;
using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.IntegrationTests.Operations;
using LupexWallet.Operations.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Runs what StatedBalanceReconciliationHostedService does at startup. The test host removes every
/// IHostedService (LupexWalletApiFactory), so a test that needs the reconciliation drives the
/// command itself, in its own scope under the System actor, like the real service.
/// Also holds the helpers of the stated-balance tests: the operation list, the raw wallet invariant of
/// ADR-0003, §6 (read straight from the tables, independent of the application's own read paths) and the
/// session advisory lock that forces a command to queue behind the wallet lock.
/// </summary>
public static class StatedBalanceTestHelpers
{
    public static async Task RunCorrectionsReconciliationAsync(
        this LupexWalletApiFactory factory, Guid walletId, CancellationToken cancellationToken = default)
    {
        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>().SetSystemActor("StatedBalanceTestReconciliation");
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new ReconcileWalletCorrectionsCommand(walletId), cancellationToken);
    }

    public static Task RunCorrectionsReconciliationAsync(
        this ApiTestFixture fixture, Guid walletId, CancellationToken cancellationToken = default) =>
        fixture.Factory.RunCorrectionsReconciliationAsync(walletId, cancellationToken);

    public static decimal ParseAmount(this OperationMoneyResponse money) =>
        decimal.Parse(money.Amount, CultureInfo.InvariantCulture);

    private sealed record OperationPage(List<OperationResponse> Data, PageMeta Pagination);
    private sealed record PageMeta(string? NextCursor, bool HasMore);

    /// <summary>GET /operations of a wallet, all pages, in the order of the API (a correction follows the other rows of its day).</summary>
    public static async Task<IReadOnlyList<OperationResponse>> ListOperationsAsync(
        this HttpClient client, Guid walletId, int limit = 100, string extraQuery = "")
    {
        var all = new List<OperationResponse>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/operations?walletId={walletId}&limit={limit}{extraQuery}"
                + (cursor is null ? string.Empty : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var page = (await response.Content.ReadFromJsonAsync<OperationPage>())!;
            all.AddRange(page.Data);
            cursor = page.Pagination.HasMore ? page.Pagination.NextCursor : null;
        }
        while (cursor is not null);

        return all;
    }

    /// <summary>The correction of the date as listed by GET /operations, or null when it is zero (hidden) or absent.</summary>
    public static async Task<OperationResponse?> ListedCorrectionAsync(this HttpClient client, Guid walletId, DateOnly date) =>
        (await client.ListOperationsAsync(walletId)).SingleOrDefault(x => x.IsCorrection && x.OperationDate == date);

    /// <summary>The amount of the correction of the date straight from the table (zero corrections included).</summary>
    public static async Task<decimal?> CorrectionAmountAsync(this ApiTestFixture fixture, Guid walletId, DateOnly date)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT amount FROM operations.operations WHERE wallet_id = @w AND operation_date = @d AND is_correction";
        command.Parameters.AddWithValue("w", walletId);
        command.Parameters.AddWithValue("d", date);
        return (decimal?)await command.ExecuteScalarAsync();
    }

    public static async Task<long> CorrectionRowCountAsync(this ApiTestFixture fixture, Guid walletId)
    {
        await using var connection = await fixture.OpenConnectionAsync();
        return await connection.CountAsync(
            "SELECT count(*) FROM operations.operations WHERE wallet_id = @w AND is_correction", ("w", walletId));
    }

    /// <summary>
    /// The invariant of ADR-0003, §6 read from the tables: CurrentBalance must equal InitialBalance plus the sum
    /// of AppliedDelta of every non-deleted operation of the wallet, corrections included.
    /// </summary>
    public static async Task AssertWalletInvariantAsync(this ApiTestFixture fixture, Guid walletId, string because = "")
    {
        await using var connection = await fixture.OpenConnectionAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT w.current_balance_amount, " +
            "       w.initial_balance_amount + COALESCE((SELECT sum(o.applied_delta_amount) FROM operations.operations o " +
            "                                            WHERE o.wallet_id = w.id AND o.deleted_at IS NULL), 0) " +
            "FROM wallets.wallets w WHERE w.id = @w";
        command.Parameters.AddWithValue("w", walletId);
        await using var reader = await command.ExecuteReaderAsync();
        (await reader.ReadAsync()).Should().BeTrue("the wallet exists");
        var current = reader.GetDecimal(0);
        var expected = reader.GetDecimal(1);
        current.Should().Be(expected, $"CurrentBalance = initial + sum of applied deltas {because}".Trim());
    }

    /// <summary>
    /// Holds the per-wallet advisory lock (the one every command of the wallet takes, ADR-0001, §4) on a separate
    /// connection until disposed, so a request for the wallet must queue behind it.
    /// </summary>
    public static async Task<IAsyncDisposable> HoldWalletLockAsync(this ApiTestFixture fixture, Guid walletId)
    {
        var connection = await fixture.OpenConnectionAsync();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT pg_advisory_lock(hashtextextended(@w::text, 0))";
            command.Parameters.AddWithValue("w", walletId.ToString());
            await command.ExecuteNonQueryAsync();
        }

        return new WalletLockHolder(connection, walletId);
    }

    /// <summary>
    /// Holds the wallet lock, sends the requests one by one (each must reach the advisory lock before the next is sent, so
    /// the grant order after the release is the send order), then releases the lock and returns the responses in send order.
    /// Unlike a command that takes the lock first, a command that touches the wallet row before locking proceeds up to
    /// that point and queues holding the row, which is the state in which a lock-order deadlock forms.
    /// </summary>
    public static async Task<HttpResponseMessage[]> SendQueuedBehindWalletLockAsync(
        this ApiTestFixture fixture, Guid walletId, params Func<Task<HttpResponseMessage>>[] sends)
    {
        var tasks = new List<Task<HttpResponseMessage>>();
        try
        {
            await using (await fixture.HoldWalletLockAsync(walletId))
            {
                foreach (var send in sends)
                {
                    tasks.Add(send());
                    await fixture.WaitForAdvisoryLockWaitersAsync(tasks.Count, tasks.ToArray());
                }
            }
        }
        catch
        {
            await Task.WhenAll(tasks.Select(task => task.ContinueWith(_ => { })));
            throw;
        }

        return await Task.WhenAll(tasks);
    }

    /// <summary>Waits until at least <paramref name="expected"/> backends are blocked on an advisory lock.</summary>
    public static async Task WaitForAdvisoryLockWaitersAsync(this ApiTestFixture fixture, int expected, params Task[] requests)
    {
        await using var monitor = await fixture.OpenConnectionAsync();
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await monitor.CountAsync(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                "AND wait_event_type = 'Lock' AND wait_event = 'advisory' AND pid <> pg_backend_pid()");
            if (waiting >= expected)
            {
                return;
            }

            requests.Any(r => r.IsCompleted).Should().BeFalse("the requests must queue behind the held wallet lock");
            await Task.Delay(20);
        }

        throw new TimeoutException($"Fewer than {expected} backends waited for the wallet advisory lock within 30 s.");
    }

    /// <summary>
    /// The number of table/index locks held by the backends waiting for an advisory lock. With <paramref name="writeLocksOnly"/>
    /// only the locks taken by INSERT/UPDATE/DELETE count. A command that takes the wallet lock before it touches anything
    /// holds none while it waits; a command that wrote before locking (or locks only later, in a subscriber such as the
    /// snapshot cascade) holds write locks on the tables it already changed.
    /// </summary>
    public static async Task<long> CountTableLocksHeldByAdvisoryWaitersAsync(this ApiTestFixture fixture, bool writeLocksOnly)
    {
        await using var monitor = await fixture.OpenConnectionAsync();
        return await monitor.CountAsync(
            "SELECT count(*) FROM pg_locks l " +
            "JOIN pg_stat_activity a ON a.pid = l.pid " +
            "JOIN pg_class c ON c.oid = l.relation " +
            "JOIN pg_namespace n ON n.oid = c.relnamespace " +
            "WHERE l.granted AND l.locktype = 'relation' AND a.datname = current_database() " +
            "AND a.wait_event_type = 'Lock' AND a.wait_event = 'advisory' " +
            (writeLocksOnly ? "AND l.mode IN ('RowExclusiveLock', 'ShareRowExclusiveLock', 'ExclusiveLock') " : string.Empty) +
            "AND n.nspname IN ('operations', 'wallets', 'reference_data', 'balance_history', 'audit', 'exchange_rates')");
    }

    private sealed class WalletLockHolder(NpgsqlConnection connection, Guid walletId) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT pg_advisory_unlock(hashtextextended(@w::text, 0))";
                command.Parameters.AddWithValue("w", walletId.ToString());
                await command.ExecuteNonQueryAsync();
            }

            await connection.DisposeAsync();
        }
    }
}
