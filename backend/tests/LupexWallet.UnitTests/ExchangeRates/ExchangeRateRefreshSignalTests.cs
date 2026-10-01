using System.Transactions;
using FluentAssertions;
using LupexWallet.ExchangeRates.Infrastructure;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// ExchangeRateRefreshSignal (ADR-0001 §7, PROGRESS R13): event subscribers run inside the publisher's
/// TransactionScope, so inside a transaction the "refresh requested" signal is written to the channel
/// only after the commit and never on a rollback; without a transaction it is written at once.
/// </summary>
public sealed class ExchangeRateRefreshSignalTests
{
    private static readonly TimeSpan CompletionTimeout = TimeSpan.FromSeconds(10);

    private readonly ExchangeRateRefreshSignal _signal = new();

    private int Drain()
    {
        var count = 0;
        while (_signal.Reader.TryRead(out _))
        {
            count++;
        }

        return count;
    }

    /// <summary>Waits until the ambient transaction has completed; subscribed after the signal's own handler, so it runs after it.</summary>
    private static ManualResetEventSlim CompletionWaiter()
    {
        var completed = new ManualResetEventSlim();
        Transaction.Current!.TransactionCompleted += (_, _) => completed.Set();
        return completed;
    }

    [Fact]
    public void RequestRefresh_WithoutTransaction_WritesImmediately()
    {
        Transaction.Current.Should().BeNull();

        _signal.RequestRefresh();

        _signal.Reader.TryRead(out var kind).Should().BeTrue();
        kind.Should().Be(ExchangeRateRefreshSignalKind.RefreshRequested);
    }

    [Fact]
    public void RequestRefresh_InsideTransaction_IsDeliveredOnlyAfterCommit()
    {
        ManualResetEventSlim completed;
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            _signal.RequestRefresh();
            completed = CompletionWaiter();

            _signal.Reader.TryRead(out _).Should().BeFalse("the transaction has not committed yet: a background pass would read uncommitted data");
            scope.Complete();
        }

        completed.Wait(CompletionTimeout).Should().BeTrue();
        Drain().Should().Be(1);
    }

    [Fact]
    public void RequestRefresh_InsideTransactionThatRollsBack_IsNeverDelivered()
    {
        ManualResetEventSlim completed;
        using (new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            _signal.RequestRefresh();
            completed = CompletionWaiter();

            // No Complete(): disposing the scope rolls the transaction back.
        }

        completed.Wait(CompletionTimeout).Should().BeTrue();
        Drain().Should().Be(0);
    }

    [Fact]
    public void RequestRefresh_SeveralCallsInOneCommittedTransaction_WriteOneSignalEach()
    {
        ManualResetEventSlim completed;
        using (var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            _signal.RequestRefresh();
            _signal.RequestRefresh();
            _signal.RequestRefresh();
            completed = CompletionWaiter();
            scope.Complete();
        }

        completed.Wait(CompletionTimeout).Should().BeTrue();
        Drain().Should().Be(3, "the background service collapses the burst into one pass when it reads the channel");
    }

    [Fact]
    public void NotifyManualRefreshCompleted_WritesManualKindImmediatelyEvenInsideTransaction()
    {
        using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

        _signal.NotifyManualRefreshCompleted();

        _signal.Reader.TryRead(out var kind).Should().BeTrue();
        kind.Should().Be(ExchangeRateRefreshSignalKind.ManualRefreshCompleted);
    }
}
