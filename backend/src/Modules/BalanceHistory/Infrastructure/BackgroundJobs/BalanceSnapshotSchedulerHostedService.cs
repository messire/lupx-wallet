using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// Scheduled snapshot pass (ADR-0003, §3/§4): at startup and daily at 12:00 UTC, for every
/// wallet, sends BackfillWalletBalanceSnapshotsCommand, which recalculates from (latest
/// snapshot date + 1), or from AccountingStartDate if there are no snapshots yet, up to today.
/// Both triggers run the same pass, so a failed wallet is healed by the next run.
///
/// Each wallet is processed in its own DI scope: DbContext, change tracker and event buffers
/// are not shared between wallets, so a failure on one wallet (its TransactionScope rolls back
/// the database, but not the change tracker) cannot leak tracked entities into the next wallet's
/// transaction. The outer scope is used only to list wallet ids. The command goes through
/// TransactionBehavior (ADR-0001, §4/§6): a wallet's snapshots, their AuditEntry records and
/// the advisory lock share one transaction.
///
/// A failure on one wallet does not interrupt the rest (BackfillWalletSafelyAsync). A
/// whole-pass failure (e.g. the DB is unavailable) is caught in RunPassAsync: BackgroundService
/// would otherwise stop the host on an unhandled exception from ExecuteAsync.
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

    /// <summary>
    /// A single pass over all wallets. Public so that integration tests can run a pass
    /// without waiting for the schedule (the hosted services are disabled in the test host).
    /// </summary>
    public async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WalletId> walletIds;
            await using (var listScope = scopeFactory.CreateAsyncScope())
            {
                var walletDirectory = listScope.ServiceProvider.GetRequiredService<IWalletDirectory>();
                walletIds = await walletDirectory.GetAllWalletIdsAsync(cancellationToken);
            }

            foreach (var walletId in walletIds)
            {
                await BackfillWalletSafelyAsync(walletId, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A whole-pass failure must not bring the host down; the next run or restart retries.
            logger.LogError(ex, "Плановый прогон создания/досоздания слепков баланса не выполнен");
        }
    }

    private async Task BackfillWalletSafelyAsync(WalletId walletId, CancellationToken cancellationToken)
    {
        try
        {
            // Own scope per wallet: separate DbContext/ChangeTracker/event buffers, so snapshots of
            // a failed wallet stay out of the next wallet's transaction (they would lack AuditEntry,
            // ADR-0006).
            await using var walletScope = scopeFactory.CreateAsyncScope();

            // ADR-0006: mark the scope as System before sending, otherwise AuditEntry records
            // default to User (AuditActorAccessor).
            walletScope.ServiceProvider.GetRequiredService<IAuditActorAccessor>()
                .SetSystemActor(nameof(BalanceSnapshotSchedulerHostedService));

            var sender = walletScope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new BackfillWalletBalanceSnapshotsCommand(walletId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Создание/досоздание слепков баланса для кошелька {WalletId} не выполнено", walletId);
        }
    }
}
