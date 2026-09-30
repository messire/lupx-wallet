using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.IntegrationTests.Audit;
using LupexWallet.IntegrationTests.BalanceHistory;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace LupexWallet.IntegrationTests.Infrastructure;

/// <summary>
/// Shared HTTP/SQL helpers for the record lifecycle tests (ADR-0002): wallet balance, snapshots,
/// audit entries, snapshot backfill and raw xmin reads. Assumes the caller has already called
/// <see cref="IntegrationTestBase.AuthenticateAsync"/>.
/// </summary>
public static class RecordLifecycleTestHelpers
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public static async Task<decimal> GetWalletBalanceAsync(this HttpClient client, Guid walletId)
    {
        var response = await client.GetAsync($"/api/v1/wallets/{walletId}");
        response.EnsureSuccessStatusCode();
        var wallet = await response.Content.ReadFromJsonAsync<WalletResponse>();
        return decimal.Parse(wallet!.CurrentBalance.Amount, CultureInfo.InvariantCulture);
    }

    /// <summary>Balance on a date via GET /wallets/{id}/balance?date= (snapshot-independent path, IWalletOperationsLookup).</summary>
    public static async Task<decimal> GetWalletBalanceOnDateAsync(this HttpClient client, Guid walletId, DateOnly date)
    {
        var response = await client.GetAsync($"/api/v1/wallets/{walletId}/balance?date={date:yyyy-MM-dd}");
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<BalanceSnapshotResponse>();
        return decimal.Parse(body!.Balance.Amount, CultureInfo.InvariantCulture);
    }

    public static async Task<Guid> CreateOperationReturningIdAsync(
        this HttpClient client, Guid walletId, Guid operationTypeId, decimal amount, DateOnly date, string? adjustmentMode = null)
    {
        var response = await client.PostAsJsonAsync("/api/v1/operations", new
        {
            WalletId = walletId,
            OperationTypeId = operationTypeId,
            Amount = amount,
            OperationDate = date,
            AdjustmentMode = adjustmentMode,
        });
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Materialized snapshots of the wallet in [from..to] as date -> balance (GET /wallets/{id}/balance-history).</summary>
    public static async Task<IReadOnlyList<BalanceSnapshotResponse>> GetSnapshotsAsync(
        this HttpClient client, Guid walletId, DateOnly from, DateOnly to)
    {
        var response = await client.GetAsync(
            $"/api/v1/wallets/{walletId}/balance-history?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&limit=100");
        response.EnsureSuccessStatusCode();
        var page = await response.Content.ReadFromJsonAsync<BalanceSnapshotPageResponse>();
        return page!.Data;
    }

    public static async Task<IReadOnlyList<AuditEntryResponse>> GetAuditEntriesAsync(
        this HttpClient client, string entityType, Guid entityId)
    {
        var entries = new List<AuditEntryResponse>();
        string? cursor = null;
        do
        {
            var url = $"/api/v1/audit-entries?entityType={entityType}&entityId={entityId}&limit=100";
            if (cursor is not null)
            {
                url += $"&cursor={Uri.EscapeDataString(cursor)}";
            }

            var response = await client.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var page = (await response.Content.ReadFromJsonAsync<AuditEntryPageResponse>())!;
            entries.AddRange(page.Data);
            cursor = page.Pagination.HasMore ? page.Pagination.NextCursor : null;
        }
        while (cursor is not null);

        return entries;
    }

    /// <summary>
    /// Materializes the snapshots of a wallet exactly as the scheduled pass does, through the command
    /// (TransactionBehavior: snapshots and their audit entries in one transaction).
    /// </summary>
    public static async Task BackfillSnapshotsAsync(this ApiTestFixture fixture, Guid walletId)
    {
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ISender>()
            .Send(new BackfillWalletBalanceSnapshotsCommand(new WalletId(walletId)));
    }

    public static async Task<NpgsqlConnection> OpenConnectionAsync(this ApiTestFixture fixture)
    {
        var connection = new NpgsqlConnection(fixture.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    public static async Task<long> CountAsync(this NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return (long)(await command.ExecuteScalarAsync())!;
    }

    public static async Task<List<string>> ReadXminsAsync(
        this NpgsqlConnection connection, string sql, params (string Name, object Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }

    public static async Task ExecuteAsync(this NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }
}
