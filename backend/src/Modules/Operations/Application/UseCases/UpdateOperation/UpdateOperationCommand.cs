using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>UC-13 (docs/api/openapi.yaml: PATCH /operations/{id}) — cascading history
/// recalculation (Q4) is BalanceHistory's responsibility, reacting to OperationUpdated
/// (ADR-0008). WalletId change is implemented as a move, equivalent to deleting from the old
/// wallet and creating on the new one (see UpdateOperationCommandHandler.Handle).</summary>
public sealed record UpdateOperationCommand(
    Guid Id,
    Guid WalletId,
    Guid OperationTypeId,
    decimal Amount,
    DateOnly OperationDate,
    AdjustmentMode? AdjustmentMode) : IRequest<OperationDto>, ICommand<OperationDto>;

public sealed class UpdateOperationCommandHandler(
    IOperationRepository repository,
    IOperationsUnitOfWork unitOfWork,
    IOperationTypeLookup operationTypeLookup,
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<UpdateOperationCommand, OperationDto>
{
    public async Task<OperationDto> Handle(UpdateOperationCommand request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByIdAsync(new OperationId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Operation {request.Id} не найдена.");

        var operationTypeId = new OperationTypeId(request.OperationTypeId);
        var operationType = await operationTypeLookup.GetAsync(operationTypeId, cancellationToken)
            ?? throw new OperationTypeReferenceNotFoundException(request.OperationTypeId);

        if (!operationType.IsActive)
        {
            throw new OperationTypeReferenceInactiveException(request.OperationTypeId);
        }

        var effectKind = BehaviorKindMapping.Parse(operationType.BehaviorKindCode);
        var newWalletId = new WalletId(request.WalletId);

        if (newWalletId == operation.WalletId)
        {
            var wallet = await walletBalanceGateway.GetAsync(operation.WalletId, cancellationToken)
                ?? throw new WalletReferenceNotFoundException(operation.WalletId.Value);

            OperationDateValidator.EnsureInRange(request.OperationDate, wallet.AccountingStartDate);

            var amount = new Money(request.Amount, wallet.CurrencyId);
            var previouslyApplied = operation.AppliedDelta;
            var baseline = OperationEffectCalculator.ComputeBaseline(wallet.CurrentBalance, previouslyApplied);
            var intendedDelta = OperationEffectCalculator.ComputeIntendedDelta(effectKind, amount, request.AdjustmentMode, baseline);
            var incrementalDelta = OperationEffectCalculator.ComputeIncrementalDelta(intendedDelta, previouslyApplied);

            operation.Update(operationTypeId, effectKind, amount, request.OperationDate, request.AdjustmentMode, appliedDelta: intendedDelta);

            await walletBalanceGateway.ApplyDeltaAsync(operation.WalletId, incrementalDelta, cancellationToken);
        }
        else
        {
            // Перенос на другой кошелек (UC-13, решение пользователя от 2026-09-11) —
            // эквивалент удаления со старого кошелька + создания на новом:
            // 1. Реверсируем то, что операция ранее применила к СТАРОМУ кошельку.
            // 2. Вычисляем intendedDelta для НОВОГО кошелька так же, как при первичном
            //    создании (CreateOperationCommand) — baseline = его CurrentBalance, amount
            //    сконструирован с его CurrencyId (валюта операции меняется вслед за
            //    валютой нового кошелька, а не сравнивается со старой — Operation.
            //    MoveToWallet, в отличие от Update, не бросает OperationCurrencyImmutableException).
            var oldWalletId = operation.WalletId;
            var oldWalletReversal = operation.AppliedDelta.Negate();

            var newWallet = await walletBalanceGateway.GetAsync(newWalletId, cancellationToken)
                ?? throw new WalletReferenceNotFoundException(request.WalletId);

            OperationDateValidator.EnsureInRange(request.OperationDate, newWallet.AccountingStartDate);

            var amount = new Money(request.Amount, newWallet.CurrencyId);
            var intendedDelta = OperationEffectCalculator.ComputeIntendedDelta(effectKind, amount, request.AdjustmentMode, newWallet.CurrentBalance);

            operation.MoveToWallet(newWalletId, operationTypeId, effectKind, amount, request.OperationDate, request.AdjustmentMode, appliedDelta: intendedDelta);

            await walletBalanceGateway.ApplyDeltaAsync(oldWalletId, oldWalletReversal, cancellationToken);
            await walletBalanceGateway.ApplyDeltaAsync(newWalletId, intendedDelta, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return OperationDto.FromDomain(operation);
    }
}
