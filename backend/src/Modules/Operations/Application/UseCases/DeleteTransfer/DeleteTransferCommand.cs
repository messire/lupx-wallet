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
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<DeleteTransferCommand>
{
    public async Task Handle(DeleteTransferCommand request, CancellationToken cancellationToken)
    {
        var transfer = await transferRepository.GetByIdAsync(new TransferId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {request.Id} не найден.");

        var sourceOperation = await operationRepository.GetByIdAsync(transfer.SourceOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.SourceOperationId} (source) не найдена.");
        var targetOperation = await operationRepository.GetByIdAsync(transfer.TargetOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.TargetOperationId} (target) не найдена.");

        // Domain invariants (not already deleted) run before any wallet is touched.
        transfer.SoftDelete(sourceOperation, targetOperation);

        await walletBalanceGateway.ApplyDeltaAsync(transfer.SourceWalletId, sourceOperation.AppliedDelta.Negate(), cancellationToken);
        await walletBalanceGateway.ApplyDeltaAsync(transfer.TargetWalletId, targetOperation.AppliedDelta.Negate(), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
