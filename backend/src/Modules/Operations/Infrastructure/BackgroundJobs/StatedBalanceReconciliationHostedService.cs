using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// One reconciliation pass at startup (ADR-0003, §6; ADR-0001, §6): for every wallet that has
/// balance corrections sends <see cref="ReconcileWalletCorrectionsCommand"/>, which recalculates
/// the corrections and heals the current balance. It repairs the data left by earlier formats
/// (a former absolute adjustment is converted to a correction with a stale amount) and any
/// divergence since.
///
/// Each wallet is processed in its own DI scope (own DbContext and change tracker), so a failure
/// on one wallet cannot leak tracked entities into the next one. The actor is System. A failure of
/// a wallet or of the whole pass is logged and never stops the host.
/// </summary>
public sealed class StatedBalanceReconciliationHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<StatedBalanceReconciliationHostedService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => RunPassAsync(stoppingToken);

    /// <summary>A single pass over the wallets with corrections. Public so that tests can run it directly (hosted services are disabled in the test host).</summary>
    public async Task RunPassAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<WalletId> walletIds;
            await using (var listScope = scopeFactory.CreateAsyncScope())
            {
                var repository = listScope.ServiceProvider.GetRequiredService<IOperationRepository>();
                walletIds = await repository.GetWalletIdsWithCorrectionsAsync(cancellationToken);
            }

            foreach (var walletId in walletIds)
            {
                await ReconcileWalletSafelyAsync(walletId, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Сверка корректирующих сумм не выполнена");
        }
    }

    private async Task ReconcileWalletSafelyAsync(WalletId walletId, CancellationToken cancellationToken)
    {
        try
        {
            await using var walletScope = scopeFactory.CreateAsyncScope();

            walletScope.ServiceProvider.GetRequiredService<IAuditActorAccessor>()
                .SetSystemActor(nameof(StatedBalanceReconciliationHostedService));

            var sender = walletScope.ServiceProvider.GetRequiredService<ISender>();
            await sender.Send(new ReconcileWalletCorrectionsCommand(walletId.Value), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Сверка корректирующих сумм кошелька {WalletId} не выполнена", walletId);
        }
    }
}
