using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>
/// UC-14 (docs/api/openapi.yaml: POST /operations/{id}/restore) — revokes soft deletion and
/// re-applies AppliedDelta. Date, type activity and wallet archival are deliberately not
/// re-checked: restoration is always possible (ADR-0002, §3).
/// </summary>
public sealed record RestoreOperationCommand(Guid Id) : IRequest<OperationDto>, ICommand<OperationDto>;

public sealed class RestoreOperationCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<RestoreOperationCommand, OperationDto>
{
    public async Task<OperationDto> Handle(RestoreOperationCommand request, CancellationToken cancellationToken)
    {
        var operationId = new OperationId(request.Id);

        var walletId = await repository.FindWalletIdAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");
        await unitOfWork.AcquireWalletLocksAsync([walletId], cancellationToken);

        var operation = await repository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        operation.Restore();

        // One net delta: the restored effect plus the change of the wallet's balance corrections.
        var effect = operation.AppliedDelta;
        var correctionsChange = await correctionsReconciler.ReconcileAsync(operation.WalletId, [operation], cancellationToken);
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, operation.WalletId, effect.Add(new Money(correctionsChange, effect.CurrencyId)), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationDto.FromDomain(operation);
    }
}
