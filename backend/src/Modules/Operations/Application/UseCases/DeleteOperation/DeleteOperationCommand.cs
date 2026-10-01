using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-14 (docs/api/openapi.yaml: DELETE /operations/{id}) — soft delete with any date (ADR-0002, §3).</summary>
public sealed record DeleteOperationCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteOperationCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<DeleteOperationCommand>
{
    public async Task Handle(DeleteOperationCommand request, CancellationToken cancellationToken)
    {
        var operationId = new OperationId(request.Id);

        var walletId = await repository.FindWalletIdAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");
        await unitOfWork.AcquireWalletLocksAsync([walletId], cancellationToken);

        var operation = await repository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        // Domain invariants (not a correction, not a transfer leg, not already deleted) run before the wallet is touched.
        operation.SoftDelete();

        // One net delta: the reversal of the operation plus the change of the wallet's balance corrections.
        var reversal = operation.AppliedDelta.Negate();
        var correctionsChange = await correctionsReconciler.ReconcileAsync(operation.WalletId, [operation], cancellationToken);
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, operation.WalletId, reversal.Add(new Money(correctionsChange, reversal.CurrencyId)), cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
