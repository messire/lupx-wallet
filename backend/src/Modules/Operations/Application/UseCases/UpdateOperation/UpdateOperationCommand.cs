using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-13 (docs/api/openapi.yaml: PATCH /operations/{id}) — cascading history
/// recalculation (Q4) is BalanceHistory's responsibility, reacting to OperationUpdated
/// (ADR-0003). WalletId change is implemented as a move, equivalent to deleting from the old
/// wallet and creating on the new one (see UpdateOperationCommandHandler.Handle).</summary>
public sealed record UpdateOperationCommand(
    Guid Id,
    Guid WalletId,
    Guid OperationTypeId,
    decimal Amount,
    DateOnly OperationDate,
    AdjustmentMode? AdjustmentMode,
    OperationsDomainException? AdjustmentModeError = null) : IRequest<OperationDto>, ICommand<OperationDto>;

public sealed class UpdateOperationCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IOperationTypeLookup operationTypeLookup,
    IWalletBalanceGateway walletBalanceGateway,
    IWalletCorrectionsReconciler correctionsReconciler) : IRequestHandler<UpdateOperationCommand, OperationDto>
{
    public async Task<OperationDto> Handle(UpdateOperationCommand request, CancellationToken cancellationToken)
    {
        var operationId = new OperationId(request.Id);
        var newWalletId = new WalletId(request.WalletId);

        // Locks come first, in ascending WalletId order, before anything is read: the current
        // wallet is found without tracking, the aggregate is loaded only under the lock.
        var currentWalletId = await repository.FindWalletIdAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");
        await unitOfWork.AcquireWalletLocksAsync([currentWalletId, newWalletId], cancellationToken);

        var operation = await repository.GetByIdAsync(operationId, cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        // Moved to another wallet between the lookup and the lock: the held lock is the wrong one.
        if (operation.WalletId != currentWalletId)
        {
            throw new RecordModifiedConcurrentlyException();
        }

        // A correction, a transfer leg or a soft-deleted operation is not editable (incl. moving to
        // another wallet; ADR-0002, §3; ADR-0003, §6); fail before any IWalletBalanceGateway call so no balance is touched.
        operation.EnsureEditable();

        // A rejected adjustmentMode is reported only now, so a correction or a deleted operation answers 409 first.
        if (request.AdjustmentModeError is not null)
        {
            throw request.AdjustmentModeError;
        }

        var operationTypeId = new OperationTypeId(request.OperationTypeId);
        var operationType = await operationTypeLookup.GetAsync(operationTypeId, cancellationToken)
            ?? throw new OperationTypeReferenceNotFoundException(request.OperationTypeId);

        if (!operationType.IsActive)
        {
            throw new OperationTypeReferenceInactiveException(request.OperationTypeId);
        }

        var effectKind = BehaviorKindMapping.Parse(operationType.BehaviorKindCode);

        if (newWalletId == operation.WalletId)
        {
            var wallet = await walletBalanceGateway.GetAsync(operation.WalletId, cancellationToken)
                ?? throw new WalletReferenceNotFoundException(operation.WalletId.Value);

            OperationDateValidator.EnsureInRange(request.OperationDate, wallet.AccountingStartDate);

            var amount = new Money(request.Amount, wallet.CurrencyId);
            var previouslyApplied = operation.AppliedDelta;
            var intendedDelta = OperationEffectCalculator.ComputeIntendedDelta(effectKind, amount, request.AdjustmentMode);
            var incrementalDelta = OperationEffectCalculator.ComputeIncrementalDelta(intendedDelta, previouslyApplied);

            operation.Update(operationTypeId, effectKind, amount, request.OperationDate, request.AdjustmentMode, appliedDelta: intendedDelta);

            var correctionsChange = await correctionsReconciler.ReconcileAsync(operation.WalletId, [operation], cancellationToken);
            await WalletBalanceChange.ApplyAsync(
                walletBalanceGateway, operation.WalletId,
                incrementalDelta.Add(new Money(correctionsChange, incrementalDelta.CurrencyId)), cancellationToken);
        }
        else
        {
            // Moving to another wallet (UC-13) is equivalent to deleting from the old wallet and
            // creating on the new one:
            // 1. The old wallet gets the reversal of what the operation applied to it.
            // 2. The intended delta for the new wallet is computed as on a primary create — the
            //    amount carries the new wallet's currency (the operation's currency follows the new
            //    wallet; Operation.MoveToWallet, unlike Update, does not enforce currency immutability).
            // Both wallets then get one net delta each, including their corrections' change.
            var oldWalletId = operation.WalletId;
            var oldWalletReversal = operation.AppliedDelta.Negate();

            var newWallet = await walletBalanceGateway.GetAsync(newWalletId, cancellationToken)
                ?? throw new WalletReferenceNotFoundException(request.WalletId);

            OperationDateValidator.EnsureInRange(request.OperationDate, newWallet.AccountingStartDate);

            var amount = new Money(request.Amount, newWallet.CurrencyId);
            var intendedDelta = OperationEffectCalculator.ComputeIntendedDelta(effectKind, amount, request.AdjustmentMode);

            operation.MoveToWallet(newWalletId, operationTypeId, effectKind, amount, request.OperationDate, request.AdjustmentMode, appliedDelta: intendedDelta);

            var oldCorrectionsChange = await correctionsReconciler.ReconcileAsync(oldWalletId, [operation], cancellationToken);
            var newCorrectionsChange = await correctionsReconciler.ReconcileAsync(newWalletId, [operation], cancellationToken);

            await WalletBalanceChange.ApplyAsync(
                walletBalanceGateway, oldWalletId,
                oldWalletReversal.Add(new Money(oldCorrectionsChange, oldWalletReversal.CurrencyId)), cancellationToken);
            await WalletBalanceChange.ApplyAsync(
                walletBalanceGateway, newWalletId,
                intendedDelta.Add(new Money(newCorrectionsChange, intendedDelta.CurrencyId)), cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationDto.FromDomain(operation);
    }
}
