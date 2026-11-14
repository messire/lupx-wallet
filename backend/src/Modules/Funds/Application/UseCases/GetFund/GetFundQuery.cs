using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>GET /funds/{id}.</summary>
public sealed record GetFundQuery(Guid Id) : IRequest<FundDto?>, IQuery<FundDto?>;

public sealed class GetFundQueryHandler(IFundRepository repository, FundViewBuilder viewBuilder)
    : IRequestHandler<GetFundQuery, FundDto?>
{
    public async Task<FundDto?> Handle(GetFundQuery request, CancellationToken cancellationToken)
    {
        var fund = await repository.GetByIdAsync(new FundId(request.Id), cancellationToken);
        return fund is null ? null : await viewBuilder.BuildAsync(fund, cancellationToken);
    }
}
