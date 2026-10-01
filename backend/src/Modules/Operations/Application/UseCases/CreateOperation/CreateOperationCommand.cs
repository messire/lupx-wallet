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
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<CreateOperationCommand, OperationDto>
{
    public async Task<OperationDto> Handle(CreateOperationCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);
        await unitOfWork.AcquireWalletLocksAsync([walletId], cancellationToken);

        var operationTypeId = new OperationTypeId(request.OperationTypeId);
        var operationType = await operationTypeLookup.GetAsync(operationTypeId, cancellationToken)
            ?? throw new OperationTypeReferenceNotFoundException(request.OperationTypeId);

        if (!operationType.IsActive)
        {
            throw new OperationTypeReferenceInactiveException(request.OperationTypeId);
        }

        var effectKind = BehaviorKindMapping.Parse(operationType.BehaviorKindCode);

        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new WalletReferenceNotFoundException(request.WalletId);

        OperationDateValidator.EnsureInRange(request.OperationDate, wallet.AccountingStartDate);

        var amount = new Money(request.Amount, wallet.CurrencyId);
        var intendedDelta = OperationEffectCalculator.ComputeIntendedDelta(effectKind, amount, request.AdjustmentMode);

        var operation = Operation.Create(
            OperationId.New(), walletId, operationTypeId, effectKind, amount,
            request.OperationDate, request.AdjustmentMode, appliedDelta: intendedDelta);

        // One net delta: the operation itself plus the change of the wallet's balance corrections.
        var correctionsChange = await correctionsReconciler.ReconcileAsync(walletId, [operation], cancellationToken);
        await WalletBalanceChange.ApplyAsync(
            walletBalanceGateway, walletId, intendedDelta.Add(new Money(correctionsChange, intendedDelta.CurrencyId)), cancellationToken);

        repository.Add(operation);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationDto.FromDomain(operation);
    }
}
