using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Daily pass (rates refresh, then history load) plus out-of-band passes on a refresh request signalled
/// after the publisher's transaction commits (ADR-0004 §3, §5, §9). A manual refresh restarts the daily
/// countdown and triggers only the history load. A failed run is logged and never stops the host.
/// </summary>
public sealed class ExchangeRateRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    ExchangeRateRefreshSignal refreshSignal,
    ILogger<ExchangeRateRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new ExchangeRateRefreshTimer(DateTimeOffset.UtcNow);

        await RunPassAsync(stoppingToken);
        timer.Reset(DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = timer.GetDelayUntilNextRun(DateTimeOffset.UtcNow);
            var signal = await WaitForSignalOrTimeoutAsync(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            if (signal == ExchangeRateRefreshSignalKind.ManualRefreshCompleted)
            {
                // The endpoint already refreshed the rates; restart the countdown and catch up the history,
                // otherwise repeated manual refreshes would keep postponing the scheduled load (ADR-0004 §3, §9).
                await RunBackfillAsync(stoppingToken);
                timer.Reset(DateTimeOffset.UtcNow);
                continue;
            }

            // A refresh request (nothing has been refreshed yet) or an elapsed delay (the
            // scheduled run): the same pass.
            await RunPassAsync(stoppingToken);
            timer.Reset(DateTimeOffset.UtcNow);
        }
    }

    /// <summary>
    /// Waits for a signal without consuming it on timeout (WaitToReadAsync only peeks), then reads
    /// everything queued: a burst from one command (WalletCreated + CurrencyCreated + ...) yields one
    /// pass. A refresh request wins over a manual-refresh notification, since it needs real work.
    /// </summary>
    private async Task<ExchangeRateRefreshSignalKind?> WaitForSignalOrTimeoutAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        timeoutCts.CancelAfter(delay);

        try
        {
            if (!await refreshSignal.Reader.WaitToReadAsync(timeoutCts.Token)
                || !refreshSignal.Reader.TryRead(out var signal))
            {
                return null;
            }

            while (refreshSignal.Reader.TryRead(out var queued))
            {
                if (queued == ExchangeRateRefreshSignalKind.RefreshRequested)
                {
                    signal = queued;
                }
            }

            return signal;
        }
        catch (OperationCanceledException)
        {
            // Either shutdown (the caller checks the token) or the delay elapsed.
            return null;
        }
    }

    /// <summary>
    /// Refresh, then history load — each in its own DI scope with its own DbContexts and the System
    /// actor (ADR-0006). A failure of the refresh must not skip the history load, nor the reverse.
    /// The manual refresh button runs from the HTTP endpoint in its own (User) scope and does not
    /// pass through here.
    /// </summary>
    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        await RunSafelyAsync(
            new RefreshExchangeRatesCommand(IsManualTrigger: false), "Обновление курсов валют", cancellationToken);
        await RunBackfillAsync(cancellationToken);
    }

    private Task RunBackfillAsync(CancellationToken cancellationToken) =>
        RunSafelyAsync(new BackfillExchangeRateHistoryCommand(), "Дозагрузка истории курсов валют", cancellationToken);

    private async Task RunSafelyAsync<TResponse>(IRequest<TResponse> command, string description, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>()
                .SetSystemActor(nameof(ExchangeRateRefreshBackgroundService));

            await scope.ServiceProvider.GetRequiredService<ISender>().Send(command, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            // A failure of the whole pass (e.g. the database is unavailable) must not bring the host
            // down — the next scheduled pass (in 24 hours) or the next signal tries again.
            logger.LogError(ex, "{Operation} не выполнено", description);
        }
    }
}
