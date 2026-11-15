using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>
/// DELETE /planned-transactions/{id} (ADR-0010 §8): a plan without executions is deleted physically;
/// a plan with executions is finished instead (an already finished one stays as is).
/// </summary>
public sealed record DeletePlannedTransactionCommand(Guid Id) : IRequest<DeletePlannedTransactionResult>, ICommand<DeletePlannedTransactionResult>;

/// <param name="FinishedPlan">Set when the plan was kept and finished; null when it was deleted.</param>
public sealed record DeletePlannedTransactionResult(PlannedTransactionDto? FinishedPlan);

public sealed class DeletePlannedTransactionCommandHandler(IPlannedTransactionRepository repository, IPlanningUnitOfWork unitOfWork)
    : IRequestHandler<DeletePlannedTransactionCommand, DeletePlannedTransactionResult>
{
    public async Task<DeletePlannedTransactionResult> Handle(DeletePlannedTransactionCommand request, CancellationToken cancellationToken)
    {
        var plan = await repository.GetForUpdateAsync(new PlannedTransactionId(request.Id), cancellationToken)
            ?? throw new PlannedTransactionNotFoundException();

        if (plan.HasExecutions)
        {
            if (plan.IsActive)
            {
                plan.Finish();
                await unitOfWork.SaveChangesAsync(cancellationToken);
            }

            return new DeletePlannedTransactionResult(PlannedTransactionDto.FromDomain(plan));
        }

        plan.MarkAsDeleted();
        repository.Remove(plan);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new DeletePlannedTransactionResult(null);
    }
}
