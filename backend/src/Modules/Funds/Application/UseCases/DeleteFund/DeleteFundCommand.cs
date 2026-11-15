using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>DELETE /funds/{id}: physical delete, only for a fund that never had a reserve (ADR-0010 §8).</summary>
public sealed record DeleteFundCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteFundCommandHandler(IFundRepository repository, IFundsUnitOfWork unitOfWork)
    : IRequestHandler<DeleteFundCommand>
{
    public async Task Handle(DeleteFundCommand request, CancellationToken cancellationToken)
    {
        var fund = await repository.GetByIdAsync(new FundId(request.Id), cancellationToken)
            ?? throw new FundNotFoundException();

        fund.EnsureCanBeDeleted();
        fund.MarkAsDeleted();
        repository.Remove(fund);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
