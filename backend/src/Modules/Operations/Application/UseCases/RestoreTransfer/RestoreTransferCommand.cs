using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>
/// UC-16 (docs/api/openapi.yaml: POST /transfers/{id}/restore) — revokes soft deletion of the
/// transfer and both legs, re-applying each leg's AppliedDelta (ADR-0002, §3).
/// </summary>
public sealed record RestoreTransferCommand(Guid Id) : IRequest<TransferDto>, ICommand<TransferDto>;

public sealed class RestoreTransferCommandHandler(
    ITransferRepository transferRepository,
    IOperationRepository operationRepository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<RestoreTransferCommand, TransferDto>
{
    public async Task<TransferDto> Handle(RestoreTransferCommand request, CancellationToken cancellationToken)
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

        transfer.Restore(sourceOperation, targetOperation);

        // One net delta per wallet: the restored leg plus the change of that wallet's balance corrections.
        Operation[] legs = [sourceOperation, targetOperation];
        var sourceCorrectionsChange = await correctionsReconciler.ReconcileAsync(transfer.SourceWalletId, legs, cancellationToken);
        var targetCorrectionsChange = await correctionsReconciler.ReconcileAsync(transfer.TargetWalletId, legs, cancellationToken);

        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, transfer.SourceWalletId,
            sourceOperation.AppliedDelta.Add(new Money(sourceCorrectionsChange, sourceOperation.CurrencyId)), cancellationToken);
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, transfer.TargetWalletId,
            targetOperation.AppliedDelta.Add(new Money(targetCorrectionsChange, targetOperation.CurrencyId)), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TransferDto.FromDomain(transfer);
    }
}
