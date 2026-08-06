using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-11/UC-12/UC-24 (docs/api/openapi.yaml: POST /operations).</summary>
public sealed record CreateOperationCommand(
    Guid WalletId,
    Guid OperationTypeId,
    decimal Amount,
    DateOnly OperationDate,
    AdjustmentMode? AdjustmentMode) : IRequest<OperationDto>, ICommand<OperationDto>;

public sealed class CreateOperationCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IOperationTypeLookup operationTypeLookup,
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<CreateOperationCommand, OperationDto>
{
    public async Task<OperationDto> Handle(CreateOperationCommand request, CancellationToken cancellationToken)
    {
        var operationTypeId = new OperationTypeId(request.OperationTypeId);
        var operationType = await operationTypeLookup.GetAsync(operationTypeId, cancellationToken)
            ?? throw new OperationTypeReferenceNotFoundException(request.OperationTypeId);

        if (!operationType.IsActive)
        {
            throw new OperationTypeReferenceInactiveException(request.OperationTypeId);
        }

        var effectKind = BehaviorKindMapping.Parse(operationType.BehaviorKindCode);

        var walletId = new WalletId(request.WalletId);
        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new WalletReferenceNotFoundException(request.WalletId);

        OperationDateValidator.EnsureInRange(request.OperationDate, wallet.AccountingStartDate);

        var amount = new Money(request.Amount, wallet.CurrencyId);
        var baseline = wallet.CurrentBalance; // AppliedDelta = 0 при создании — baseline = currentBalance
        var intendedDelta = OperationEffectCalculator.ComputeIntendedDelta(effectKind, amount, request.AdjustmentMode, baseline);

        var operation = Operation.Create(
            OperationId.New(), walletId, operationTypeId, effectKind, amount,
            request.OperationDate, request.AdjustmentMode, appliedDelta: intendedDelta);

        await walletBalanceGateway.ApplyDeltaAsync(walletId, intendedDelta, cancellationToken);

        repository.Add(operation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationDto.FromDomain(operation);
    }
}
