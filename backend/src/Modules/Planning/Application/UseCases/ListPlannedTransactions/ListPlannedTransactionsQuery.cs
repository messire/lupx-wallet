using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Planning.Application;

/// <summary>GET /planned-transactions — cursor pagination by creation order; status null — all.</summary>
public sealed record ListPlannedTransactionsQuery(PlannedTransactionStatus? Status, string? Cursor, int Limit)
    : IRequest<PlannedTransactionPageDto>, IQuery<PlannedTransactionPageDto>;

public sealed class ListPlannedTransactionsQueryHandler(IPlannedTransactionRepository repository)
    : IRequestHandler<ListPlannedTransactionsQuery, PlannedTransactionPageDto>
{
    public async Task<PlannedTransactionPageDto> Handle(ListPlannedTransactionsQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);
        var page = await repository.ListAsync(
            request.Status, PlannedTransactionCursor.TryDecode(request.Cursor), limit, cancellationToken);

        var nextCursor = page.HasMore && page.Items.Count > 0
            ? new PlannedTransactionCursor(page.Items[^1].CreatedAt).Encode()
            : null;

        return new PlannedTransactionPageDto(page.Items.Select(PlannedTransactionDto.FromDomain).ToList(), nextCursor, page.HasMore);
    }
}
