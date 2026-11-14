using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.ExchangeRates.Infrastructure;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// ExchangeRateRefreshBackgroundService (ADR-0004 p.3/9, ADR-0001 §7): a pass is Refresh, then Backfill,
/// each in its own scope with the System actor; signals queued before the loop reads them are drained,
/// a refresh request wins over a manual-refresh notification (which runs only the history load), a burst
/// gives one pass. Signals are queued
/// before StartAsync and the doubles complete synchronously, so the whole run up to "waiting for the next
/// signal" happens inside StartAsync — no sleeps.
/// </summary>
public sealed class ExchangeRateRefreshBackgroundServiceTests
{
    private const string Refresh = nameof(RefreshExchangeRatesCommand);
    private const string Backfill = nameof(BackfillExchangeRateHistoryCommand);

    private readonly ISender _sender = Substitute.For<ISender>();
    private readonly IAuditActorAccessor _actorAccessor = Substitute.For<IAuditActorAccessor>();
    private readonly ExchangeRateRefreshSignal _signal = new();

    private ExchangeRateRefreshBackgroundService CreateService()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _sender);
        services.AddScoped(_ => _actorAccessor);
        var provider = services.BuildServiceProvider();
        return new ExchangeRateRefreshBackgroundService(
            provider.GetRequiredService<IServiceScopeFactory>(), _signal, NullLogger<ExchangeRateRefreshBackgroundService>.Instance);
    }

    private async Task<IReadOnlyList<string>> RunAsync(params ExchangeRateRefreshSignalKind[] queuedSignals)
    {
        foreach (var kind in queuedSignals)
        {
            if (kind == ExchangeRateRefreshSignalKind.RefreshRequested)
            {
                _signal.RequestRefresh();
            }
            else
            {
                _signal.NotifyManualRefreshCompleted();
            }
        }

        var service = CreateService();
        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        return _sender.ReceivedCalls()
            .Select(call => call.GetArguments()[0]!.GetType().Name)
            .ToList();
    }

    [Fact]
    public async Task Start_RunsOnePassRefreshThenBackfillWithSystemActor()
    {
        var sent = await RunAsync();

        sent.Should().Equal(Refresh, Backfill);
        _actorAccessor.Received(2).SetSystemActor(nameof(ExchangeRateRefreshBackgroundService));
    }

    [Fact]
    public async Task Start_RefreshCommandIsNotManual()
    {
        await RunAsync();

        var command = _sender.ReceivedCalls().Select(c => c.GetArguments()[0]).OfType<RefreshExchangeRatesCommand>().Single();
        command.IsManualTrigger.Should().BeFalse();
    }

    [Fact]
    public async Task Start_RefreshFails_BackfillStillRuns()
    {
        _sender.Send(Arg.Any<IRequest<RefreshExchangeRatesResult>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<RefreshExchangeRatesResult>(new InvalidOperationException("database down")));

        var sent = await RunAsync();

        sent.Should().Equal(Refresh, Backfill);
    }

    [Fact]
    public async Task QueuedRefreshRequest_TriggersOneMorePass()
    {
        var sent = await RunAsync(ExchangeRateRefreshSignalKind.RefreshRequested);

        sent.Should().Equal(Refresh, Backfill, Refresh, Backfill);
    }

    [Fact]
    public async Task QueuedManualRefreshNotification_RunsOnlyTheHistoryLoadInTheSystemScope()
    {
        var sent = await RunAsync(ExchangeRateRefreshSignalKind.ManualRefreshCompleted);

        sent.Should().Equal(Refresh, Backfill, Backfill);
        _actorAccessor.Received(3).SetSystemActor(nameof(ExchangeRateRefreshBackgroundService));
    }

    [Fact]
    public async Task ManualNotificationAfterRefreshRequest_DoesNotCancelTheRequestedPass()
    {
        var sent = await RunAsync(ExchangeRateRefreshSignalKind.RefreshRequested, ExchangeRateRefreshSignalKind.ManualRefreshCompleted);

        sent.Should().Equal(Refresh, Backfill, Refresh, Backfill);
    }

    [Fact]
    public async Task RefreshRequestBetweenManualNotifications_WinsOverThem()
    {
        var sent = await RunAsync(
            ExchangeRateRefreshSignalKind.ManualRefreshCompleted,
            ExchangeRateRefreshSignalKind.RefreshRequested,
            ExchangeRateRefreshSignalKind.ManualRefreshCompleted);

        sent.Should().Equal(Refresh, Backfill, Refresh, Backfill);
    }

    [Fact]
    public async Task BurstOfRefreshRequests_IsDrainedIntoOnePass()
    {
        var sent = await RunAsync(
            ExchangeRateRefreshSignalKind.RefreshRequested,
            ExchangeRateRefreshSignalKind.RefreshRequested,
            ExchangeRateRefreshSignalKind.RefreshRequested);

        sent.Should().Equal(Refresh, Backfill, Refresh, Backfill);
        _signal.Reader.TryRead(out _).Should().BeFalse("the burst was read in full");
    }
}
