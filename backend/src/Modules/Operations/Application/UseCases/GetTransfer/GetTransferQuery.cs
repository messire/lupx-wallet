using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>GET /transfers/{id} (docs/api/openapi.yaml).</summary>
public sealed record GetTransferQuery(Guid Id) : IRequest<TransferDto?>, IQuery<TransferDto?>;

public sealed class GetTransferQueryHandler(ITransferRepository repository) : IRequestHandler<GetTransferQuery, TransferDto?>
{
    public async Task<TransferDto?> Handle(GetTransferQuery request, CancellationToken cancellationToken)
    {
        var transfer = await repository.GetByIdAsync(new TransferId(request.Id), cancellationToken);
        return transfer is null ? null : TransferDto.FromDomain(transfer);
    }
}
