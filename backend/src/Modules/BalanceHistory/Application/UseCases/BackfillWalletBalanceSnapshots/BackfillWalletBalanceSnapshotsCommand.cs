using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// Scheduled/startup balance snapshot pass for a single wallet (ADR-0003, §3/§4): creates the
/// missing snapshots and refreshes the existing ones from (latest snapshot date + 1), or from
/// AccountingStartDate if the wallet has no snapshots yet, up to today. Sent by
/// BalanceSnapshotSchedulerHostedService once per wallet so that, as a command, it goes through
/// TransactionBehavior (ADR-0001, §4/§6): the snapshot writes, their AuditEntry records (Audit
/// subscribers run from SavedChangesAsync) and the wallet advisory lock share one transaction.
/// </summary>
public sealed record BackfillWalletBalanceSnapshotsCommand(WalletId WalletId) : IRequest, ICommand<Unit>;

public sealed class BackfillWalletBalanceSnapshotsCommandHandler(
    IBalanceSnapshotRepository repository,
    IWalletBalanceGateway walletGateway,
    BalanceRecalculationService recalculationService) : IRequestHandler<BackfillWalletBalanceSnapshotsCommand>
{
    public async Task Handle(BackfillWalletBalanceSnapshotsCommand request, CancellationToken cancellationToken)
    {
        var wallet = await walletGateway.GetAsync(request.WalletId, cancellationToken);
        if (wallet is null)
        {
            // A wallet can't be physically removed while it has operations/snapshots
            // (ddd-model.md §5) — a defensive check for a race with another process.
            return;
        }

        var latestSnapshotDate = await repository.GetLatestSnapshotDateAsync(request.WalletId, cancellationToken);
        var fromDate = latestSnapshotDate?.AddDays(1) ?? wallet.AccountingStartDate;

        await recalculationService.RecalculateFromAsync(request.WalletId, fromDate, cancellationToken);
    }
}
