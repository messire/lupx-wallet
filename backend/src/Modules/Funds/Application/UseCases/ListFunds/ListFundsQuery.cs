using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>GET /funds, ordered by creation time; closed funds only with includeClosed.</summary>
public sealed record ListFundsQuery(bool IncludeClosed, string? Cursor, int Limit) : IRequest<FundPageDto>, IQuery<FundPageDto>;

public sealed class ListFundsQueryHandler(IFundRepository repository, FundViewBuilder viewBuilder)
    : IRequestHandler<ListFundsQuery, FundPageDto>
{
    public async Task<FundPageDto> Handle(ListFundsQuery request, CancellationToken cancellationToken)
    {
        var cursor = FundCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(request.IncludeClosed, cursor, limit, cancellationToken);
        var items = await viewBuilder.BuildAsync(page.Items, cancellationToken);

        var nextCursor = page.HasMore ? new FundCursor(page.Items[^1].CreatedAt).Encode() : null;
        return new FundPageDto(items, nextCursor, page.HasMore);
    }
}
