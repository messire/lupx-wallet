using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Реализует ADR-0004 (плановое создание слепка в 12:00 UTC + досоздание пропущенных
/// запусков при старте) поверх общего BalanceRecalculationService (ADR-0003/ADR-0004:
/// оба триггера обязаны использовать один и тот же алгоритм расчета баланса на дату).
///
/// Оба триггера (старт приложения и ежедневный прогон в 12:00 UTC) выполняют один и тот
/// же проход — для каждого кошелька пересчет от (дата последнего слепка + 1) или от
/// AccountingStartDate, если слепков еще нет; RecalculateFromAsync сам каскадно доводит
/// пересчет до сегодняшнего дня. Это делает ежедневный прогон самовосстанавливающимся:
/// если один прогон для кошелька упал (сеть, БД), пропуск закроется следующим прогоном,
/// а не только следующим перезапуском приложения (BackfillMissedSnapshotsAsync раньше
/// была отдельной, "более полной" логикой, чем RunDailySnapshotAsync — это расхождение
/// устранено, теперь используется один и тот же метод).
///
/// Ошибка на одном кошельке не прерывает обработку остальных (RecalculateWalletSafelyAsync).
/// Ошибка на уровне всего прохода (например, БД недоступна при старте или в момент 12:00
/// UTC) не должна ронять хост целиком — BackgroundService по умолчанию останавливает весь
/// процесс при необработанном исключении из ExecuteAsync, поэтому каждый проход обернут
/// в try/catch на уровне RunPassAsync.
/// </summary>
public sealed class BalanceSnapshotSchedulerHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<BalanceSnapshotSchedulerHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunPassAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var delay = NextRunDelay(DateTimeOffset.UtcNow);
            try
            {
                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }

            await RunPassAsync(stoppingToken);
        }
    }

    internal static TimeSpan NextRunDelay(DateTimeOffset nowUtc)
    {
        var todayNoon = new DateTimeOffset(nowUtc.Year, nowUtc.Month, nowUtc.Day, 12, 0, 0, TimeSpan.Zero);
        var nextRun = nowUtc < todayNoon ? todayNoon : todayNoon.AddDays(1);
        return nextRun - nowUtc;
    }

    private async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();

            // ADR-0010: помечаем этот scope как System до отправки любых команд — иначе
            // AuditEntry, порождённые пересчётом внутри этого прохода, по умолчанию были бы
            // помечены как User (см. AuditActorAccessor).
            scope.ServiceProvider.GetRequiredService<IAuditActorAccessor>()
                .SetSystemActor(nameof(BalanceSnapshotSchedulerHostedService));

            var walletDirectory = scope.ServiceProvider.GetRequiredService<IWalletDirectory>();
            var walletGateway = scope.ServiceProvider.GetRequiredService<IWalletBalanceGateway>();
            var snapshotRepository = scope.ServiceProvider.GetRequiredService<IBalanceSnapshotRepository>();
            var recalculationService = scope.ServiceProvider.GetRequiredService<BalanceRecalculationService>();

            var walletIds = await walletDirectory.GetAllWalletIdsAsync(cancellationToken);
            foreach (var walletId in walletIds)
            {
                await RecalculateWalletSafelyAsync(
                    walletId, walletGateway, snapshotRepository, recalculationService, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Сбой на уровне всего прохода (например, БД недоступна) не должен ронять хост —
            // следующий плановый прогон (через 24 часа) или следующий перезапуск попробуют снова.
            logger.LogError(ex, "Плановый прогон создания/досоздания слепков баланса не выполнен");
        }
    }

    private async Task RecalculateWalletSafelyAsync(
        WalletId walletId,
        IWalletBalanceGateway walletGateway,
        IBalanceSnapshotRepository snapshotRepository,
        BalanceRecalculationService recalculationService,
        CancellationToken cancellationToken)
    {
        try
        {
            var wallet = await walletGateway.GetAsync(walletId, cancellationToken);
            if (wallet is null)
            {
                return;
            }

            var latestSnapshotDate = await snapshotRepository.GetLatestSnapshotDateAsync(walletId, cancellationToken);
            var fromDate = latestSnapshotDate?.AddDays(1) ?? wallet.AccountingStartDate;

            await recalculationService.RecalculateFromAsync(walletId, fromDate, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Создание/досоздание слепков баланса для кошелька {WalletId} не выполнено", walletId);
        }
    }
}
