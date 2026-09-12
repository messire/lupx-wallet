using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Operations.Application;

/// <summary>GET /operations/{id} (docs/api/openapi.yaml).</summary>
public sealed record GetOperationQuery(Guid Id) : IRequest<OperationDto?>, IQuery<OperationDto?>;

public sealed class GetOperationQueryHandler(IOperationRepository repository) : IRequestHandler<GetOperationQuery, OperationDto?>
{
    public async Task<OperationDto?> Handle(GetOperationQuery request, CancellationToken cancellationToken)
    {
        var operation = await repository.GetByIdAsync(new OperationId(request.Id), cancellationToken);
        return operation is null ? null : OperationDto.FromDomain(operation);
    }
}
