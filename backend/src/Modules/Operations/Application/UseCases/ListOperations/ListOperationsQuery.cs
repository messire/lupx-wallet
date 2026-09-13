using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Operations.Application;

public sealed record OperationPageDto(IReadOnlyList<OperationDto> Data, string? NextCursor, bool HasMore);

/// <summary>UC-11/UC-12 list (docs/api/openapi.yaml: GET /operations).</summary>
public sealed record ListOperationsQuery(
    Guid? WalletId,
    Guid? OperationTypeId,
    DateOnly? DateFrom,
    DateOnly? DateTo,
    string? Cursor,
    int Limit) : IRequest<OperationPageDto>, IQuery<OperationPageDto>;

public sealed class ListOperationsQueryHandler(IOperationRepository repository) : IRequestHandler<ListOperationsQuery, OperationPageDto>
{
    public async Task<OperationPageDto> Handle(ListOperationsQuery request, CancellationToken cancellationToken)
    {
        var cursor = OperationCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var filter = new OperationListFilter(
            request.WalletId is { } w ? new WalletId(w) : null,
            request.OperationTypeId is { } ot ? new OperationTypeId(ot) : null,
            request.DateFrom,
            request.DateTo);

        var page = await repository.ListAsync(filter, cursor, limit, cancellationToken);

        var items = page.Items.Select(OperationDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new OperationCursor(page.Items[^1].OperationDate, page.Items[^1].CreatedAt).Encode()
            : null;

        return new OperationPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
