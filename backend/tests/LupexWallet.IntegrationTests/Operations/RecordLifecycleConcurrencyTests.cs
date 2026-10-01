using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using Npgsql;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Two concurrent requests against the same record (ADR-0002, §3): commands on a wallet are serialized by
/// the wallet advisory lock (ADR-0001, §4), so the loser reads the committed state and is rejected with 409 by
/// the lifecycle rules (deleted_at stays a concurrency token as a safety net). Exactly one of them wins, the
/// loser's whole transaction (including the wallet delta) rolls back — the balance delta is applied once and
/// the audit trail has a single entry.
/// The race is forced deterministically: the test holds a row lock on the record, both requests are in flight
/// (one blocked on the record's UPDATE, the other on the wallet lock), then the lock is released.
/// </summary>
public sealed class RecordLifecycleConcurrencyTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private HttpClient CreateSecondClient()
    {
        var client = Fixture.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Fixture.Token);
        return client;
    }

    /// <summary>Sends the same request from two clients while a row lock is held; see the overload below.</summary>
    private Task<HttpResponseMessage[]> RaceAsync(
        Func<HttpClient, Task<HttpResponseMessage>> send, string lockSql, Guid recordId) =>
        RaceAsync(send, send, lockSql, recordId);

    /// <summary>
    /// Holds the row lock taken by <paramref name="lockSql"/>, sends two requests from two clients, waits until
    /// two backends are blocked on locks (both requests loaded the record as active), then releases the lock.
    /// </summary>
    private async Task<HttpResponseMessage[]> RaceAsync(
        Func<HttpClient, Task<HttpResponseMessage>> sendFirst, Func<HttpClient, Task<HttpResponseMessage>> sendSecond,
        string lockSql, Guid recordId)
    {
        using var secondClient = CreateSecondClient();
        await using var lockConnection = await Fixture.OpenConnectionAsync();
        await using var monitorConnection = await Fixture.OpenConnectionAsync();
        await using var transaction = await lockConnection.BeginTransactionAsync();
        await using (var lockCommand = new NpgsqlCommand(lockSql, lockConnection, transaction))
        {
            lockCommand.Parameters.AddWithValue("id", recordId);
            (await lockCommand.ExecuteScalarAsync()).Should().NotBeNull("the record to lock exists");
        }

        var first = sendFirst(Client);
        var second = sendSecond(secondClient);

        try
        {
            await WaitForBlockedBackendsAsync(monitorConnection, expected: 2, first, second);
        }
        finally
        {
            await transaction.CommitAsync();
        }

        return await Task.WhenAll(first, second);
    }

    /// <summary>
    /// Polls pg_stat_activity on a connection outside any transaction (inside one the view is a frozen snapshot).
    /// </summary>
    private static async Task WaitForBlockedBackendsAsync(NpgsqlConnection monitorConnection, int expected, params Task[] requests)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() " +
                "AND wait_event_type = 'Lock' AND pid <> pg_backend_pid()", monitorConnection);
            if ((long)(await command.ExecuteScalarAsync())! >= expected)
            {
                return;
            }

            // A request that finished before blocking means the race was not reproduced.
            requests.Any(r => r.IsCompleted).Should().BeFalse("both requests must reach the row lock before it is released");
            await Task.Delay(20);
        }

        throw new TimeoutException($"Fewer than {expected} backends blocked on the row lock within 30 s.");
    }

    private static async Task AssertOneWinnerAsync(HttpResponseMessage[] responses, HttpStatusCode winnerStatus)
    {
        responses.Select(r => r.StatusCode).Should().BeEquivalentTo(
            new[] { winnerStatus, HttpStatusCode.Conflict }, "exactly one request wins, the other is rejected as a conflict");

        var loser = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        var problem = await loser.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("detail").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task DeleteOperation_TwoConcurrentRequests_OneSucceedsOtherConflictsAndDeltaIsAppliedOnce()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m);

        var responses = await RaceAsync(
            c => c.DeleteAsync($"/api/v1/operations/{operationId}"),
            "SELECT id FROM operations.operations WHERE id = @id FOR UPDATE", operationId);

        await AssertOneWinnerAsync(responses, HttpStatusCode.NoContent);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m, "the reversal of 300 is applied once, not twice");
        var audit = await Client.GetAuditEntriesAsync("Operation", operationId);
        audit.Count(e => e.Action == "Deleted").Should().Be(1);
    }

    [Fact]
    public async Task RestoreOperation_TwoConcurrentRequests_OneSucceedsOtherConflictsAndDeltaIsAppliedOnce()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));
        (await Client.DeleteAsync($"/api/v1/operations/{operationId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(1000m);

        var responses = await RaceAsync(
            c => c.PostAsync($"/api/v1/operations/{operationId}/restore", null),
            "SELECT id FROM operations.operations WHERE id = @id FOR UPDATE", operationId);

        await AssertOneWinnerAsync(responses, HttpStatusCode.OK);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(1300m, "the delta of 300 is re-applied once, not twice");
        var audit = await Client.GetAuditEntriesAsync("Operation", operationId);
        audit.Count(e => e.Action == "Restored").Should().Be(1);
    }

    [Fact]
    public async Task UpdateOperation_ConcurrentWithDelete_OneSucceedsOtherConflictsAndBalanceStaysConsistent()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 1000m, accountingStartDate: Today.AddDays(-10));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-3));

        var responses = await RaceAsync(
            c => c.DeleteAsync($"/api/v1/operations/{operationId}"),
            c => c.PatchAsJsonAsync($"/api/v1/operations/{operationId}", new
            {
                WalletId = wallet.Id,
                OperationTypeId = incomeTypeId,
                Amount = 500m,
                OperationDate = Today.AddDays(-3),
                AdjustmentMode = (string?)null,
            }),
            "SELECT id FROM operations.operations WHERE id = @id FOR UPDATE", operationId);

        // Serialized by the wallet lock: delete first rejects the edit (409), edit first lets the delete follow (both succeed).
        responses.Should().OnlyContain(r => r.IsSuccessStatusCode || r.StatusCode == HttpStatusCode.Conflict);
        responses.Count(r => r.IsSuccessStatusCode).Should().BeGreaterThanOrEqualTo(1);
        var operation = (await (await Client.GetAsync($"/api/v1/operations/{operationId}")).Content.ReadFromJsonAsync<OperationResponse>())!;
        var expectedBalance = operation.IsDeleted ? 1000m : 1500m;
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(expectedBalance,
            "the balance reflects exactly the state of the winning request");
    }

    [Fact]
    public async Task DeleteTransfer_TwoConcurrentRequests_OneSucceedsOtherConflictsAndDeltasAreAppliedOnce()
    {
        await AuthenticateAsync();
        var currency = await Client.CreateCurrencyAsync("CC" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var source = await Client.CreateWalletAsync(500m, Today.AddDays(-10), "Источник", walletType.Id, currency.Id);
        var target = await Client.CreateWalletAsync(100m, Today.AddDays(-10), "Назначение", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        var created = await Client.PostAsJsonAsync("/api/v1/transfers", new
        {
            SourceWalletId = source.Id,
            TargetWalletId = target.Id,
            Amount = 150m,
            TransferDate = Today.AddDays(-3),
        });
        created.EnsureSuccessStatusCode();
        var transferId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        (await Client.GetWalletBalanceAsync(source.Id)).Should().Be(350m);
        (await Client.GetWalletBalanceAsync(target.Id)).Should().Be(250m);

        var responses = await RaceAsync(
            c => c.DeleteAsync($"/api/v1/transfers/{transferId}"),
            "SELECT id FROM operations.transfers WHERE id = @id FOR UPDATE", transferId);

        await AssertOneWinnerAsync(responses, HttpStatusCode.NoContent);
        (await Client.GetWalletBalanceAsync(source.Id)).Should().Be(500m);
        (await Client.GetWalletBalanceAsync(target.Id)).Should().Be(100m);
        var audit = await Client.GetAuditEntriesAsync("Transfer", transferId);
        audit.Count(e => e.Action == "Deleted").Should().Be(1);
    }
}
