using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Recalculates every balance correction of the wallet and brings its current balance back to the
/// invariant: initial balance + the effect of all non-deleted operations, corrections included
/// (ADR-0003, §6). Run at startup, so it also heals the data left by earlier formats; changes
/// nothing — no events, no audit — when everything already agrees.
/// </summary>
public sealed record ReconcileWalletCorrectionsCommand(Guid WalletId) : IRequest, ICommand<Unit>;

public sealed class ReconcileWalletCorrectionsCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<ReconcileWalletCorrectionsCommand>
{
    public async Task Handle(ReconcileWalletCorrectionsCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);
        await unitOfWork.AcquireWalletLocksAsync([walletId], cancellationToken);

        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken);
        if (wallet is null || !await repository.AnyCorrectionsAsync(walletId, cancellationToken))
        {
            return;
        }

        await correctionsReconciler.ReconcileAsync(walletId, [], cancellationToken);

        // The reconciler updated the tracked corrections; the expected balance follows from them.
        var corrections = await repository.GetCorrectionsAsync(walletId, cancellationToken);
        var regular = await repository.GetRegularLedgerAsync(walletId, [], null, cancellationToken);
        var expected = wallet.InitialBalance.Amount
            + regular.Sum(x => x.AppliedDelta)
            + corrections.Sum(x => x.AppliedDelta.Amount);

        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, walletId, new Money(expected - wallet.CurrentBalance.Amount, wallet.CurrencyId), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
