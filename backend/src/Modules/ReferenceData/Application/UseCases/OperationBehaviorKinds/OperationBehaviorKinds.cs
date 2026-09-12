using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ReferenceData.Application;

public sealed record OperationBehaviorKindDto(Guid Id, string Code, string Name)
{
    public static OperationBehaviorKindDto FromDomain(OperationBehaviorKind entity) =>
        new(entity.Id.Value, entity.Code, entity.Name);
}

/// <summary>Read-only — the set is fixed at deployment (ddd-model.md, §2.3).</summary>
public interface IOperationBehaviorKindRepository
{
    Task<IReadOnlyList<OperationBehaviorKind>> ListAllAsync(CancellationToken cancellationToken);
}

public sealed record ListOperationBehaviorKindsQuery : IRequest<IReadOnlyList<OperationBehaviorKindDto>>, IQuery<IReadOnlyList<OperationBehaviorKindDto>>;

public sealed class ListOperationBehaviorKindsQueryHandler(IOperationBehaviorKindRepository repository)
    : IRequestHandler<ListOperationBehaviorKindsQuery, IReadOnlyList<OperationBehaviorKindDto>>
{
    public async Task<IReadOnlyList<OperationBehaviorKindDto>> Handle(
        ListOperationBehaviorKindsQuery request, CancellationToken cancellationToken)
    {
        var kinds = await repository.ListAllAsync(cancellationToken);
        return kinds.Select(OperationBehaviorKindDto.FromDomain).ToList();
    }
}
