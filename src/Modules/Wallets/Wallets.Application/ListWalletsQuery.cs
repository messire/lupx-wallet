using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>UC-07 (docs/api/openapi.yaml: GET /wallets).</summary>
public sealed record ListWalletsQuery(
    bool IncludeArchived,
    string? Cursor,
    int Limit) : IRequest<WalletPageDto>, IQuery<WalletPageDto>;

public sealed record WalletPageDto(IReadOnlyList<WalletDto> Data, string? NextCursor, bool HasMore);

public sealed class ListWalletsQueryHandler(IWalletRepository repository) : IRequestHandler<ListWalletsQuery, WalletPageDto>
{
    public async Task<WalletPageDto> Handle(ListWalletsQuery request, CancellationToken cancellationToken)
    {
        var cursor = WalletCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(request.IncludeArchived, cursor, limit, cancellationToken);

        var items = page.Items.Select(WalletDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new WalletCursor(page.Items[^1].DisplayOrder, page.Items[^1].CreatedAt).Encode()
            : null;

        return new WalletPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
