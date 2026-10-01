using System.Data.Common;
using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.IntegrationTests.Infrastructure;
using LupexWallet.Operations.Infrastructure;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace LupexWallet.IntegrationTests.Operations;

/// <summary>
/// WalletAdvisoryLock (ADR-0001, §4): the locks of several wallets are taken one by one in ascending Guid order, each
/// wallet once, whatever the order the caller passes them in — the rule that keeps two commands on the same pair of
/// wallets in opposite directions from deadlocking. The statements really sent to PostgreSQL are recorded.
/// </summary>
public sealed class WalletAdvisoryLockTests(ApiTestFixture fixture) : IntegrationTestBase(fixture)
{
    private sealed class LockStatementRecorder : DbCommandInterceptor
    {
        public List<string> WalletIds { get; } = [];

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                WalletIds.Add((string)command.Parameters[0].Value!);
            }

            return await base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private async Task<List<string>> AcquireAsync(params WalletId[] walletIds)
    {
        var recorder = new LockStatementRecorder();
        var options = new DbContextOptionsBuilder<OperationsDbContext>()
            .UseNpgsql(Fixture.ConnectionString)
            .AddInterceptors(recorder)
            .Options;
        await using var context = new OperationsDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        await WalletAdvisoryLock.AcquireAllAsync(context.Database, walletIds, CancellationToken.None);

        await transaction.CommitAsync();
        return recorder.WalletIds;
    }

    [Fact]
    public async Task AcquireAll_TakesEachWalletOnceInAscendingGuidOrder_WhateverTheOrderPassed()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToList();
        var shuffled = new[] { ids[3], ids[0], ids[4], ids[3], ids[1], ids[2], ids[0] };

        var taken = await AcquireAsync(shuffled.Select(x => new WalletId(x)).ToArray());

        taken.Should().Equal(ids.Order().Select(x => x.ToString()), "ascending Guid order, duplicates skipped");
    }

    [Fact]
    public async Task AcquireAll_TwoWalletsInBothDirections_TakeTheSameSequence()
    {
        var a = new WalletId(Guid.NewGuid());
        var b = new WalletId(Guid.NewGuid());

        var forward = await AcquireAsync(a, b);
        var backward = await AcquireAsync(b, a);

        forward.Should().Equal(backward, "A->B and B->A transfers must acquire in the same order");
    }

    [Fact]
    public async Task AcquireAsync_IsReentrantWithinOneTransaction()
    {
        // A held xact lock of the same transaction is re-entrant: a second acquisition by the same transaction
        // (the BalanceHistory cascade inside an Operations command) must not wait for itself.
        var walletId = new WalletId(Guid.NewGuid());
        var options = new DbContextOptionsBuilder<OperationsDbContext>().UseNpgsql(Fixture.ConnectionString).Options;
        await using var context = new OperationsDbContext(options);
        await using var transaction = await context.Database.BeginTransactionAsync();

        await WalletAdvisoryLock.AcquireAsync(context.Database, walletId, CancellationToken.None);
        var second = WalletAdvisoryLock.AcquireAsync(context.Database, walletId, CancellationToken.None);

        await second.WaitAsync(TimeSpan.FromSeconds(10));
        await transaction.CommitAsync();
    }
}
