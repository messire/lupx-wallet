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
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<RestoreTransferCommand, TransferDto>
{
    public async Task<TransferDto> Handle(RestoreTransferCommand request, CancellationToken cancellationToken)
    {
        var transfer = await transferRepository.GetByIdAsync(new TransferId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Transfer {request.Id} не найден.");

        var sourceOperation = await operationRepository.GetByIdAsync(transfer.SourceOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.SourceOperationId} (source) не найдена.");
        var targetOperation = await operationRepository.GetByIdAsync(transfer.TargetOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.TargetOperationId} (target) не найдена.");

        transfer.Restore(sourceOperation, targetOperation);

        await walletBalanceGateway.ApplyDeltaAsync(transfer.SourceWalletId, sourceOperation.AppliedDelta, cancellationToken);
        await walletBalanceGateway.ApplyDeltaAsync(transfer.TargetWalletId, targetOperation.AppliedDelta, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return TransferDto.FromDomain(transfer);
    }
}
