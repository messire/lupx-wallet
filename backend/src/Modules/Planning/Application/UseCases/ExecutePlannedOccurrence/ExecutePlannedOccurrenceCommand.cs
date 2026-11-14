using LupexWallet.Operations.Application;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>
/// POST /planned-transactions/{id}/occurrences/{date}/execute (ADR-0010 §6). Creates a regular operation
/// through Operations' IOperationRecorder in the same transaction and links it to the occurrence.
/// Defaults: the plan wallet, the remaining expected amount, the effective date of the occurrence.
/// </summary>
public sealed record ExecutePlannedOccurrenceCommand(
    Guid PlannedTransactionId,
    DateOnly OccurrenceDate,
    Guid? WalletId,
    decimal? Amount,
    DateOnly? OperationDate) : IRequest<PlannedOccurrenceExecutionResultDto>, ICommand<PlannedOccurrenceExecutionResultDto>;

public sealed class ExecutePlannedOccurrenceCommandHandler(
    IPlannedTransactionRepository repository,
    IPlanningUnitOfWork unitOfWork,
    PlannedOccurrenceReader reader,
    PlannedTransactionReferences references,
    IOperationRecorder operationRecorder) : IRequestHandler<ExecutePlannedOccurrenceCommand, PlannedOccurrenceExecutionResultDto>
{
    public async Task<PlannedOccurrenceExecutionResultDto> Handle(ExecutePlannedOccurrenceCommand request, CancellationToken cancellationToken)
    {
        var planId = new PlannedTransactionId(request.PlannedTransactionId);

        // Lock order (review v0.4.0, R7): the wallet lock first, the plan row second — as Wallets commands do (the
        // wallet lock, then their Planning subscribers update plan rows). The wallet is known before the plan is
        // locked: the requested one, or the plan's default read without a lock and checked again under it.
        var lockedWalletId = request.WalletId is { } requestedWallet ? new WalletId(requestedWallet) : await FindDefaultWalletAsync(planId, cancellationToken);
        if (lockedWalletId is { } walletToLock)
        {
            await unitOfWork.AcquireWalletLockAsync(walletToLock, cancellationToken);
        }

        var plan = await repository.GetForUpdateAsync(planId, cancellationToken)
            ?? throw new PlannedTransactionNotFoundException();

        if (request.WalletId is null && plan.WalletId != lockedWalletId)
        {
            // The default wallet changed between the read and the lock (e.g. the wallet was deleted and unlinked
            // from the plan meanwhile): the held lock is the wrong one.
            throw new PlannedTransactionModifiedConcurrentlyException();
        }

        var current = await reader.ProjectOneAsync(plan, request.OccurrenceDate, cancellationToken);
        plan.EnsureCanExecute(current);

        // Everything Operations would reject is checked here first, so the client gets Planning's 400.
        var walletId = request.WalletId is { } requested ? new WalletId(requested)
            : plan.WalletId ?? throw new PlannedExecutionWalletRequiredException();
        var wallet = await references.GetWalletInCurrencyAsync(walletId, plan.CurrencyId, cancellationToken);
        await references.ResolveDirectionAsync(plan.OperationTypeId, currentOperationTypeId: null, cancellationToken);

        var amount = request.Amount ?? current.ExpectedAmount;
        if (amount <= 0)
        {
            throw new PlannedAmountMustBePositiveException();
        }

        var operationDate = request.OperationDate ?? current.EffectiveDate;
        var today = PlannedOccurrenceReader.Today;
        if (operationDate < wallet.AccountingStartDate || operationDate > today)
        {
            throw new PlannedExecutionDateOutOfRangeException(operationDate, wallet.AccountingStartDate, today);
        }

        var recorded = await operationRecorder.RecordAsync(walletId, plan.OperationTypeId, amount, operationDate, cancellationToken);
        plan.RecordExecution(current, recorded.OperationId, recorded.Amount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        var updated = await reader.ProjectOneAsync(plan, request.OccurrenceDate, cancellationToken);
        return new PlannedOccurrenceExecutionResultDto(PlannedOccurrenceDto.From(plan, updated), recorded.OperationId.Value);
    }

    private async Task<WalletId?> FindDefaultWalletAsync(PlannedTransactionId planId, CancellationToken cancellationToken)
    {
        var (exists, walletId) = await repository.FindWalletIdAsync(planId, cancellationToken);
        return exists ? walletId : throw new PlannedTransactionNotFoundException();
    }
}
