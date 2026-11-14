using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

public sealed record GetPlannedTransactionQuery(Guid Id) : IRequest<PlannedTransactionDto?>, IQuery<PlannedTransactionDto?>;

public sealed class GetPlannedTransactionQueryHandler(IPlannedTransactionRepository repository)
    : IRequestHandler<GetPlannedTransactionQuery, PlannedTransactionDto?>
{
    public async Task<PlannedTransactionDto?> Handle(GetPlannedTransactionQuery request, CancellationToken cancellationToken)
    {
        var plan = await repository.GetByIdAsync(new PlannedTransactionId(request.Id), cancellationToken);
        return plan is null ? null : PlannedTransactionDto.FromDomain(plan);
    }
}
