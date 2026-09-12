using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-16 (docs/api/openapi.yaml: DELETE /transfers/{id}).</summary>
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

        // ADR-0009, случай b — решение пользователя от 2026-09-11, см. подробный комментарий
        // в DeleteOperationCommand. Для перевода используется его собственная TransferDate
        // (совпадает по построению с OperationDate обеих частей, см. Transfer.Create).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var hasHistory = transfer.TransferDate < today;
        transfer.EnsureCanBeDeleted(hasHistory);

        var sourceOperation = await operationRepository.GetByIdAsync(transfer.SourceOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.SourceOperationId} (source) не найдена.");
        var targetOperation = await operationRepository.GetByIdAsync(transfer.TargetOperationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {transfer.TargetOperationId} (target) не найдена.");

        await walletBalanceGateway.ApplyDeltaAsync(transfer.SourceWalletId, sourceOperation.AppliedDelta.Negate(), cancellationToken);
        await walletBalanceGateway.ApplyDeltaAsync(transfer.TargetWalletId, targetOperation.AppliedDelta.Negate(), cancellationToken);

        sourceOperation.MarkAsDeleted();
        targetOperation.MarkAsDeleted();
        operationRepository.Remove(sourceOperation);
        operationRepository.Remove(targetOperation);

        transfer.MarkAsDeleted();
        transferRepository.Remove(transfer);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
