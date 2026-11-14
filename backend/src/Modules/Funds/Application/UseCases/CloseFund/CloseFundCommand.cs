using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>POST /funds/{id}/close: releases all allocations and closes the fund (final state).</summary>
public sealed record CloseFundCommand(Guid Id) : IRequest<FundDto>, ICommand<FundDto>;

public sealed class CloseFundCommandHandler(
    IFundRepository repository,
    IFundsUnitOfWork unitOfWork,
    FundViewBuilder viewBuilder) : IRequestHandler<CloseFundCommand, FundDto>
{
    public async Task<FundDto> Handle(CloseFundCommand request, CancellationToken cancellationToken)
    {
        var fund = await repository.GetByIdAsync(new FundId(request.Id), cancellationToken)
            ?? throw new FundNotFoundException();

        fund.Close();
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await viewBuilder.BuildAsync(fund, cancellationToken);
    }
}
