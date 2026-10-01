using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// Concurrency of the commands that change the operations of a wallet (ADR-0001, §4; ADR-0003, §6). Every command
/// takes the per-wallet advisory lock before it reads anything. The tests make this observable and deterministic: the
/// test itself holds the wallet lock on a separate connection, the requests are sent, and the test waits until they are
/// queued behind it (pg_stat_activity: wait_event = advisory) before releasing it — a command without the lock would
/// not queue and the test would fail instead of passing by luck. Technique as in RecordLifecycleConcurrencyTests.
/// </summary>
public sealed class StatedBalanceConcurrencyTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private static DateOnly Today => RecordLifecycleTestHelpers.Today;

    /// <summary>
    /// Holds the lock of the wallet, sends the requests one by one (each must queue), then releases the lock. While queued a
    /// command must not have written anything (the snapshot cascade of BalanceHistory takes the same lock, but only after the
    /// operations were written, so queueing there would be too late); with <paramref name="mayHaveReadAlready"/> false it must
    /// not have touched a table at all (the commands that find the wallet of a record first read it without tracking).
    /// </summary>
    private async Task<HttpResponseMessage[]> RunQueuedBehindWalletLockAsync(
        Guid lockedWalletId, bool mayHaveReadAlready, params Func<Task<HttpResponseMessage>>[] sends)
    {
        var tasks = new List<Task<HttpResponseMessage>>();
        try
        {
            await using (await Fixture.HoldWalletLockAsync(lockedWalletId))
            {
                foreach (var send in sends)
                {
                    tasks.Add(send());
                    await Fixture.WaitForAdvisoryLockWaitersAsync(tasks.Count, tasks.ToArray());
                }

                tasks.Should().OnlyContain(t => !t.IsCompleted, "every request waits for the wallet lock");
                (await Fixture.CountTableLocksHeldByAdvisoryWaitersAsync(writeLocksOnly: mayHaveReadAlready)).Should().Be(
                    0, "a queued command has not written anything yet: the wallet lock comes first");
            }
        }
        catch
        {
            // The lock is released; let the requests finish so that they do not leak into the next test.
            await Task.WhenAll(tasks.Select(task => task.ContinueWith(_ => { })));
            throw;
        }

        return await Task.WhenAll(tasks);
    }

    private Task<HttpResponseMessage> Put(Guid walletId, DateOnly date, decimal amount) =>
        Client.PutAsJsonAsync($"/api/v1/wallets/{walletId}/stated-balances/{date:yyyy-MM-dd}", new { Amount = amount });

    private Task<HttpResponseMessage> PostOperation(Guid walletId, Guid typeId, decimal amount, DateOnly date) =>
        Client.PostAsJsonAsync("/api/v1/operations", new { WalletId = walletId, OperationTypeId = typeId, Amount = amount, OperationDate = date });

    private Task<HttpResponseMessage> Patch(Guid operationId, Guid walletId, Guid typeId, decimal amount, DateOnly date) =>
        Client.PatchAsJsonAsync($"/api/v1/operations/{operationId}", new
        {
            WalletId = walletId, OperationTypeId = typeId, Amount = amount, OperationDate = date, AdjustmentMode = (string?)null,
        });

    private Task<HttpResponseMessage> PostTransfer(Guid source, Guid target, decimal amount, DateOnly date) =>
        Client.PostAsJsonAsync("/api/v1/transfers", new { SourceWalletId = source, TargetWalletId = target, Amount = amount, TransferDate = date });

    private async Task<(WalletResponse A, WalletResponse B)> CreateTwoWalletsWithTransferTypeAsync()
    {
        var currency = await Client.CreateCurrencyAsync("CT" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant(), "Тестовая валюта");
        var walletType = await Client.CreateWalletTypeAsync();
        var a = await Client.CreateWalletAsync(1000m, Today.AddDays(-30), "A", walletType.Id, currency.Id);
        var b = await Client.CreateWalletAsync(1000m, Today.AddDays(-30), "B", walletType.Id, currency.Id);
        await Client.CreateOperationTypeIdAsync("Transfer");
        return (a, b);
    }

    // ---- Parallel statements of the same date ----

    [Fact]
    public async Task SixParallelPuts_OnTheSameDate_YieldOneRowOneCreatedAndNoServerError()
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 300m, Today.AddDays(-10));
        var day = Today.AddDays(-5);

        var responses = await Task.WhenAll(Enumerable.Range(1, 6).Select(i => Put(wallet.Id, day, 100m * i)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1, "exactly one request creates the statement");
        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(5, "the others replace it");
        (await Fixture.CorrectionRowCountAsync(wallet.Id)).Should().Be(1);
        var ids = new List<Guid>();
        foreach (var response in responses)
        {
            ids.Add((await response.Content.ReadFromJsonAsync<OperationResponse>())!.Id);
        }

        ids.Distinct().Should().ContainSingle("every response describes the same row");
        // Read by id: a zero correction (a stated balance equal to the +300 of the operations) is not listed.
        var final = (await (await Client.GetAsync($"/api/v1/operations/{ids[0]}")).Content.ReadFromJsonAsync<OperationResponse>())!;
        var finalStated = final.StatedBalance!.ParseAmount();
        new[] { 100m, 200m, 300m, 400m, 500m, 600m }.Should().Contain(finalStated);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, day)).Should().Be(finalStated);
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(finalStated);
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
        var audit = await Client.GetAuditEntriesAsync("Operation", final.Id);
        audit.Count(e => e.Action == "Created").Should().Be(1);
        audit.Count(e => e.Action == "Updated").Should().Be(5);
    }

    // ---- Every command queues behind the wallet lock ----

    [Theory]
    [InlineData("put")]
    [InlineData("create")]
    [InlineData("update")]
    [InlineData("delete")]
    [InlineData("restore")]
    public async Task OperationCommands_TakeTheWalletLockBeforeDoingAnything(string command)
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 100m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var existing = await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 50m, Today.AddDays(-10));
        if (command == "restore")
        {
            (await Client.DeleteAsync($"/api/v1/operations/{existing}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var (send, expected) = command switch
        {
            "put" => ((Func<Task<HttpResponseMessage>>)(() => Put(wallet.Id, Today.AddDays(-5), 500m)), HttpStatusCode.Created),
            "create" => (() => PostOperation(wallet.Id, incomeTypeId, 10m, Today.AddDays(-3)), HttpStatusCode.Created),
            "update" => (() => Patch(existing, wallet.Id, incomeTypeId, 70m, Today.AddDays(-10)), HttpStatusCode.OK),
            "delete" => (() => Client.DeleteAsync($"/api/v1/operations/{existing}"), HttpStatusCode.NoContent),
            _ => (() => Client.PostAsync($"/api/v1/operations/{existing}/restore", null), HttpStatusCode.OK),
        };

        var responses = await RunQueuedBehindWalletLockAsync(wallet.Id, mayHaveReadAlready: command is not ("put" or "create"), send);

        responses.Single().StatusCode.Should().Be(expected);
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
    }

    [Fact]
    public async Task MovingAnOperation_TakesTheLockOfTheTargetWalletToo()
    {
        await AuthenticateAsync();
        var (a, b) = await CreateTwoWalletsWithTransferTypeAsync();
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        var operationId = await Client.CreateOperationReturningIdAsync(a.Id, incomeTypeId, 50m, Today.AddDays(-10));

        var responses = await RunQueuedBehindWalletLockAsync(
            b.Id, mayHaveReadAlready: true, () => Patch(operationId, b.Id, incomeTypeId, 50m, Today.AddDays(-10)));

        responses.Single().StatusCode.Should().Be(HttpStatusCode.OK);
        (await Client.GetWalletBalanceAsync(a.Id)).Should().Be(1000m);
        (await Client.GetWalletBalanceAsync(b.Id)).Should().Be(1050m);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("restore")]
    public async Task TransferCommands_TakeTheLocksOfBothWallets(string command)
    {
        await AuthenticateAsync();
        var (source, target) = await CreateTwoWalletsWithTransferTypeAsync();
        var transferId = Guid.Empty;
        if (command != "create")
        {
            var created = await PostTransfer(source.Id, target.Id, 150m, Today.AddDays(-5));
            created.EnsureSuccessStatusCode();
            transferId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        if (command == "restore")
        {
            (await Client.DeleteAsync($"/api/v1/transfers/{transferId}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        var (send, expected) = command switch
        {
            "create" => ((Func<Task<HttpResponseMessage>>)(() => PostTransfer(source.Id, target.Id, 150m, Today.AddDays(-5))), HttpStatusCode.Created),
            "delete" => (() => Client.DeleteAsync($"/api/v1/transfers/{transferId}"), HttpStatusCode.NoContent),
            _ => (() => Client.PostAsync($"/api/v1/transfers/{transferId}/restore", null), HttpStatusCode.OK),
        };

        // Holding either wallet is enough: the command needs both.
        var responses = await RunQueuedBehindWalletLockAsync(target.Id, mayHaveReadAlready: command != "create", send);

        responses.Single().StatusCode.Should().Be(expected);
        await Fixture.AssertWalletInvariantAsync(source.Id);
        await Fixture.AssertWalletInvariantAsync(target.Id);
    }

    // ---- Opposite transfers: the fixed lock order prevents a deadlock ----

    [Fact]
    public async Task OppositeTransfers_AToBAndBToA_BothSucceedWithoutDeadlock()
    {
        await AuthenticateAsync();
        var (first, second) = await CreateTwoWalletsWithTransferTypeAsync();
        var (lower, higher) = first.Id.CompareTo(second.Id) < 0 ? (first.Id, second.Id) : (second.Id, first.Id);

        // The test holds the lower wallet. Both requests queue on it first (ascending order), so neither ever holds
        // the higher wallet while waiting for the lower one. With an unordered acquisition the request lower->higher
        // takes the lower wallet and waits for the higher one, which the other request holds: 40P01 and a 500.
        var responses = await RunQueuedBehindWalletLockAsync(
            lower, mayHaveReadAlready: false,
            () => PostTransfer(lower, higher, 100m, Today.AddDays(-3)),
            () => PostTransfer(higher, lower, 40m, Today.AddDays(-3)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await Client.GetWalletBalanceAsync(lower)).Should().Be(940m);
        (await Client.GetWalletBalanceAsync(higher)).Should().Be(1060m);
        await Fixture.AssertWalletInvariantAsync(lower);
        await Fixture.AssertWalletInvariantAsync(higher);
    }

    [Fact]
    public async Task OppositeTransfers_RunningFreely_BothSucceed()
    {
        await AuthenticateAsync();
        var (a, b) = await CreateTwoWalletsWithTransferTypeAsync();

        for (var round = 0; round < 3; round++)
        {
            var responses = await Task.WhenAll(
                PostTransfer(a.Id, b.Id, 10m, Today.AddDays(-3)),
                PostTransfer(b.Id, a.Id, 10m, Today.AddDays(-3)),
                PostTransfer(a.Id, b.Id, 10m, Today.AddDays(-2)),
                PostTransfer(b.Id, a.Id, 10m, Today.AddDays(-2)));

            responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created, $"round {round}");
        }

        (await Client.GetWalletBalanceAsync(a.Id)).Should().Be(1000m);
        (await Client.GetWalletBalanceAsync(b.Id)).Should().Be(1000m);
    }

    // ---- A statement and an operation racing for the same wallet ----

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StatementAndOperationQueuedOnTheSameWallet_LeaveTheBalanceOfTheDateAsStated(bool statementFirst)
    {
        await AuthenticateAsync();
        var wallet = await Client.CreateWalletAsync(initialBalanceAmount: 0m, accountingStartDate: Today.AddDays(-20));
        var incomeTypeId = await Client.CreateOperationTypeIdAsync("Income");
        await Client.CreateOperationReturningIdAsync(wallet.Id, incomeTypeId, 100m, Today.AddDays(-8));
        var day = Today.AddDays(-5);
        Func<Task<HttpResponseMessage>> statement = () => Put(wallet.Id, day, 500m);
        Func<Task<HttpResponseMessage>> operation = () => PostOperation(wallet.Id, incomeTypeId, 70m, Today.AddDays(-6));

        var responses = await RunQueuedBehindWalletLockAsync(
            wallet.Id, mayHaveReadAlready: false, statementFirst ? new[] { statement, operation } : new[] { operation, statement });

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.Created);
        (await Client.GetWalletBalanceOnDateAsync(wallet.Id, day)).Should().Be(500m, "whichever ran first, the day ends with the stated balance");
        (await Fixture.CorrectionAmountAsync(wallet.Id, day)).Should().Be(330m, "500 stated - (100 + 70)");
        (await Client.GetWalletBalanceAsync(wallet.Id)).Should().Be(500m);
        await Fixture.AssertWalletInvariantAsync(wallet.Id);
    }

    // ---- A statement that slipped past the wallet lock hits the unique index and is reported as a 409 ----

    [Fact]
    public async Task UniqueIndexViolationOfTheCorrectionDate_IsReportedAsRecordModifiedConcurrently()
    {
        var walletId = WalletId.New();
        var currencyId = new CurrencyId(Guid.NewGuid());
        var date = Today.AddDays(-3);
        var options = new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(Fixture.ConnectionString).Options;
        await using var context = new OperationsDbContext(options);
        var unitOfWork = new OperationsUnitOfWork(context);
        context.Operations.Add(Operation.CreateCorrection(OperationId.New(), walletId, currencyId, date, 100m));
        context.Operations.Add(Operation.CreateCorrection(OperationId.New(), walletId, currencyId, date, 200m));

        var act = () => unitOfWork.SaveChangesAsync(CancellationToken.None);

        await act.Should().ThrowAsync<RecordModifiedConcurrentlyException>(
            "two statements of one date can only race past the lock, and the loser must get the 409, not a 500");
    }
}
