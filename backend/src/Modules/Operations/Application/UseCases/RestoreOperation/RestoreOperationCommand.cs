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
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<RestoreOperationCommand, OperationDto>
{
    public async Task<OperationDto> Handle(RestoreOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByIdAsync(new OperationId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        operation.Restore();

        await walletBalanceGateway.ApplyDeltaAsync(operation.WalletId, operation.AppliedDelta, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationDto.FromDomain(operation);
    }
}
