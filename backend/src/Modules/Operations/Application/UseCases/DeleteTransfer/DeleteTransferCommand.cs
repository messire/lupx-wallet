using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-16 (docs/api/openapi.yaml: DELETE /transfers/{id}) — soft delete of the transfer and both legs (ADR-0002, §3).</summary>
public sealed record DeleteTransferCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteTransferCommandHandler(
    ITransferRepository transferRepository,
    IOperationRepository operationRepository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<DeleteTransferCommand>
{
    public async Task Handle(DeleteTransferCommand request, CancellationToken cancellationToken)
    {
        var transferId = new TransferId(request.Id);

        var walletIds = await transferRepository.FindWalletIdsAsync(transferId, cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {request.Id} не найден.");
        await unitOfWork.AcquireWalletLocksAsync([walletIds.SourceWalletId, walletIds.TargetWalletId], cancellationToken);

        var transfer = await transferRepository.GetByIdAsync(transferId, cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {request.Id} не найден.");

        var sourceOperation = await operationRepository.GetByIdAsync(transfer.SourceOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.SourceOperationId} (source) не найдена.");
        var targetOperation = await operationRepository.GetByIdAsync(transfer.TargetOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.TargetOperationId} (target) не найдена.");

        // Domain invariants (not already deleted) run before any wallet is touched.
        transfer.SoftDelete(sourceOperation, targetOperation);

        // One net delta per wallet: the reversal of the leg plus the change of that wallet's balance corrections.
        Operation[] legs = [sourceOperation, targetOperation];
        var sourceCorrectionsChange = await correctionsReconciler.ReconcileAsync(transfer.SourceWalletId, legs, cancellationToken);
        var targetCorrectionsChange = await correctionsReconciler.ReconcileAsync(transfer.TargetWalletId, legs, cancellationToken);

        var sourceReversal = sourceOperation.AppliedDelta.Negate();
        var targetReversal = targetOperation.AppliedDelta.Negate();
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, transfer.SourceWalletId,
            sourceReversal.Add(new Money(sourceCorrectionsChange, sourceReversal.CurrencyId)), cancellationToken);
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, transfer.TargetWalletId,
            targetReversal.Add(new Money(targetCorrectionsChange, targetReversal.CurrencyId)), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
