using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.ExchangeRates.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupexWallet.ExchangeRates.Infrastructure;

/// <summary>
/// Реализует ADR-0001 п.2/3 — суточное автообновление курсов, ручное обновление сбрасывает
/// отсчёт, реакция на PrimaryWalletChanged (ADR-0001 п.5) выполняет внеочередное обновление.
/// По аналогии с BalanceHistory.Infrastructure.BalanceSnapshotSchedulerHostedService — ошибка
/// одного прохода не должна ронять хост целиком (try/catch на уровне RunRefreshSafelyAsync).
///
/// Планирование (когда наступит следующий плановый прогон) вынесено в чистый
/// ExchangeRateRefreshTimer (ExchangeRates.Application) — тестируется без реального
/// Task.Delay. Здесь остаётся только I/O-часть: ожидание сигнала или истечения задержки,
/// выполнение самого обновления.
/// </summary>
public sealed class ExchangeRateRefreshBackgroundService(
    IServiceScopeFactory scopeFactory,
    ExchangeRateRefreshSignal refreshSignal,
    ILogger<ExchangeRateRefreshBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var timer = new ExchangeRateRefreshTimer(DateTimeOffset.UtcNow);

        await RunRefreshSafelyAsync(isManualTrigger: false, stoppingToken);
        timer.Reset(DateTimeOffset.UtcNow);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = timer.GetDelayUntilNextRun(DateTimeOffset.UtcNow);
            var signal = await WaitForSignalOrTimeoutAsync(delay, stoppingToken);

            if (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            switch (signal)
            {
                case ExchangeRateRefreshSignalKind.ManualRefreshCompleted:
                    // Ручное обновление уже выполнено эндпоинтом синхронно — просто
                    // перезапускаем суточный отсчёт (ADR-0001 п.3).
                    timer.Reset(DateTimeOffset.UtcNow);
                    break;

                case ExchangeRateRefreshSignalKind.RefreshRequested:
                    // Реакция на PrimaryWalletChanged (ADR-0001 п.5) — обновление ещё не
                    // выполнялось, делаем это здесь, вне транзакции SetPrimaryWallet (ADR-0011).
                    await RunRefreshSafelyAsync(isManualTrigger: false, stoppingToken);
                    timer.Reset(DateTimeOffset.UtcNow);
                    break;

                default:
                    // Задержка истекла без сигнала — обычный плановый прогон.
                    await RunRefreshSafelyAsync(isManualTrigger: false, stoppingToken);
                    timer.Reset(DateTimeOffset.UtcNow);
                    break;
            }
        }
    }

    private async Task<ExchangeRateRefreshSignalKind?> WaitForSignalOrTimeoutAsync(TimeSpan delay, CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var delayTask = Task.Delay(delay, cts.Token);
        var signalTask = refreshSignal.Reader.ReadAsync(cts.Token).AsTask();

        try
        {
            var completed = await Task.WhenAny(delayTask, signalTask);
            if (completed == signalTask && !signalTask.IsFaulted && !signalTask.IsCanceled)
            {
                return signalTask.Result;
            }

            return null;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return null;
        }
        finally
        {
            cts.Cancel();
        }
    }

    private async Task RunRefreshSafelyAsync(bool isManualTrigger, CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();

            // ADR-0010: этот сервис всегда вызывает RefreshExchangeRatesCommand из
            // собственного фонового scope (плановый прогон, старт, реакция на
            // PrimaryWalletChanged) — ручное обновление по кнопке идёт напрямую из HTTP-
            // эндпоинта (ExchangeRatesEndpointsExtensions), в его собственном (User) scope,
            // этого метода не касается.
            scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>()
                .SetSystemActor(nameof(ExchangeRateRefreshBackgroundService));

            var sender = scope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new RefreshExchangeRatesCommand(isManualTrigger), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Сбой на уровне всего прохода (например, БД недоступна) не должен ронять хост —
            // следующий плановый прогон (через 24 часа) или следующий сигнал попробуют снова.
            logger.LogError(ex, "Плановое/внеочередное обновление курсов валют не выполнено");
        }
    }
}
