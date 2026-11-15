using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>POST /planned-transactions/{id}/finish — the plan stops expecting occurrences (ADR-0010 §6, §8).</summary>
public sealed record FinishPlannedTransactionCommand(Guid Id) : IRequest<PlannedTransactionDto>, ICommand<PlannedTransactionDto>;

public sealed class FinishPlannedTransactionCommandHandler(IPlannedTransactionRepository repository, IPlanningUnitOfWork unitOfWork)
    : IRequestHandler<FinishPlannedTransactionCommand, PlannedTransactionDto>
{
    public async Task<PlannedTransactionDto> Handle(FinishPlannedTransactionCommand request, CancellationToken cancellationToken)
    {
        var plan = await repository.GetForUpdateAsync(new PlannedTransactionId(request.Id), cancellationToken)
            ?? throw new PlannedTransactionNotFoundException();

        plan.Finish();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return PlannedTransactionDto.FromDomain(plan);
    }
}
