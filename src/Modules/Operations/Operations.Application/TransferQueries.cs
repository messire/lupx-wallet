using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Operations.Application;

public sealed record TransferPageDto(IReadOnlyList<TransferDto> Data, string? NextCursor, bool HasMore);

/// <summary>UC-16 список переводов (docs/api/openapi.yaml: GET /transfers).</summary>
public sealed record ListTransfersQuery(Guid? WalletId, string? Cursor, int Limit)
    : IRequest<TransferPageDto>, IQuery<TransferPageDto>;

public sealed class ListTransfersQueryHandler(ITransferRepository repository) : IRequestHandler<ListTransfersQuery, TransferPageDto>
{
    public async Task<TransferPageDto> Handle(ListTransfersQuery request, CancellationToken cancellationToken)
    {
        var cursor = TransferCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);
        var walletId = request.WalletId is { } w ? new WalletId(w) : (WalletId?)null;

        var page = await repository.ListAsync(walletId, cursor, limit, cancellationToken);

        var items = page.Items.Select(TransferDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new TransferCursor(page.Items[^1].TransferDate, page.Items[^1].CreatedAt).Encode()
            : null;

        return new TransferPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}

public sealed record GetTransferQuery(Guid Id) : IRequest<TransferDto?>, IQuery<TransferDto?>;

public sealed class GetTransferQueryHandler(ITransferRepository repository) : IRequestHandler<GetTransferQuery, TransferDto?>
{
    public async Task<TransferDto?> Handle(GetTransferQuery request, CancellationToken cancellationToken)
    {
        var transfer = await repository.GetByIdAsync(new TransferId(request.Id), cancellationToken);
        return transfer is null ? null : TransferDto.FromDomain(transfer);
    }
}
