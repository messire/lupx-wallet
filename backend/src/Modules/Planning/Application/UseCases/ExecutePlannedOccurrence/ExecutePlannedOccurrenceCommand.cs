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
        var plan = await repository.GetForUpdateAsync(new PlannedTransactionId(request.PlannedTransactionId), cancellationToken)
            ?? throw new PlannedTransactionNotFoundException();

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
}
