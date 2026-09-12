using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Implements ADR-0004 (scheduled snapshot creation at 12:00 UTC + backfilling missed runs
/// on startup) on top of the shared BalanceRecalculationService (ADR-0003/ADR-0004: both
/// triggers must use the same balance-on-date algorithm).
///
/// Both triggers (application startup and the daily 12:00 UTC run) perform the same pass —
/// for each wallet, recalculate from (latest snapshot date + 1), or from
/// AccountingStartDate if there are no snapshots yet; RecalculateFromAsync itself cascades
/// the recalculation up to today. This makes the daily run self-healing: if one wallet's run
/// fails (network, DB), the gap is closed by the next run rather than only by the next
/// application restart (BackfillMissedSnapshotsAsync used to be separate, "more complete"
/// logic than RunDailySnapshotAsync — that discrepancy is now resolved, and both paths use
/// the same method).
///
/// A failure on one wallet does not interrupt processing of the rest
/// (RecalculateWalletSafelyAsync). A failure at the whole-pass level (e.g. the DB is
/// unavailable at startup or at 12:00 UTC) must not bring down the whole host —
/// BackgroundService stops the entire process by default on an unhandled exception from
/// ExecuteAsync, so each pass is wrapped in try/catch at the RunPassAsync level.
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
