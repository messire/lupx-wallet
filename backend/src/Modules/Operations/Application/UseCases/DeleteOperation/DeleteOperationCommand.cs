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
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<DeleteOperationCommand>
{
    public async Task Handle(DeleteOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByIdAsync(new OperationId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        // Domain invariants (not a transfer leg, not already deleted) run before the wallet is touched.
        operation.SoftDelete();

        await walletBalanceGateway.ApplyDeltaAsync(operation.WalletId, operation.AppliedDelta.Negate(), cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
