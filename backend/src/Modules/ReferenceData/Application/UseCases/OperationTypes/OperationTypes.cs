using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ReferenceData.Application;

public sealed record OperationTypeDto(Guid Id, string Name, Guid BehaviorKindId, bool IsActive)
{
    public static OperationTypeDto FromDomain(OperationType entity) =>
        new(entity.Id.Value, entity.Name, entity.BehaviorKindId.Value, entity.IsActive);
}

public sealed record OperationTypePageDto(IReadOnlyList<OperationTypeDto> Data, string? NextCursor, bool HasMore);

public sealed record OperationTypePageResult(IReadOnlyList<OperationType> Items, bool HasMore);

public interface IOperationTypeRepository
{
    void Add(OperationType operationType);
    void Remove(OperationType operationType);
    Task<OperationType?> GetByIdAsync(OperationTypeId id, CancellationToken cancellationToken);

    Task<OperationTypePageResult> ListAsync(
        bool includeInactive,
        OperationBehaviorKindId? behaviorKindId,
        NameCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken);
}

// ---- UC-15: добавить тип операции ----
public sealed record CreateOperationTypeCommand(string Name, Guid BehaviorKindId)
    : IRequest<OperationTypeDto>, ICommand<OperationTypeDto>;

public sealed class CreateOperationTypeCommandHandler(IOperationTypeRepository repository, IReferenceDataUnitOfWork unitOfWork)
    : IRequestHandler<CreateOperationTypeCommand, OperationTypeDto>
{
    public async Task<OperationTypeDto> Handle(CreateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var operationType = OperationType.Create(
            OperationTypeId.New(),
            request.Name,
            new OperationBehaviorKindId(request.BehaviorKindId));

        repository.Add(operationType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OperationTypeDto.FromDomain(operationType);
    }
}

public sealed record DeactivateOperationTypeCommand(Guid Id) : IRequest<OperationTypeDto>, ICommand<OperationTypeDto>;

public sealed class DeactivateOperationTypeCommandHandler(IOperationTypeRepository repository, IReferenceDataUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateOperationTypeCommand, OperationTypeDto>
{
    public async Task<OperationTypeDto> Handle(DeactivateOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var operationType = await repository.GetByIdAsync(new OperationTypeId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"OperationType {request.Id} не найден.");

        operationType.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return OperationTypeDto.FromDomain(operationType);
    }
}

public sealed record DeleteOperationTypeCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteOperationTypeCommandHandler(
    IOperationTypeRepository repository,
    IReferenceDataUnitOfWork unitOfWork,
    IEnumerable<IReferenceItemUsageProbe> usageProbes) : IRequestHandler<DeleteOperationTypeCommand>
{
    public async Task Handle(DeleteOperationTypeCommand request, CancellationToken cancellationToken)
    {
        var operationType = await repository.GetByIdAsync(new OperationTypeId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"OperationType {request.Id} не найден.");

        // ADR-0009, случай c: реальная проверка использования через реализации Operations.
        var isUsed = await usageProbes.AnyIsUsedAsync(ReferenceItemKind.OperationType, request.Id, cancellationToken);
        operationType.EnsureCanBeDeleted(isUsed);

        operationType.MarkAsDeleted();
        repository.Remove(operationType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- UC-27: список типов операций ----
public sealed record ListOperationTypesQuery(bool IncludeInactive, Guid? BehaviorKindId, string? Cursor, int Limit)
    : IRequest<OperationTypePageDto>, IQuery<OperationTypePageDto>;

public sealed class ListOperationTypesQueryHandler(IOperationTypeRepository repository)
    : IRequestHandler<ListOperationTypesQuery, OperationTypePageDto>
{
    public async Task<OperationTypePageDto> Handle(ListOperationTypesQuery request, CancellationToken cancellationToken)
    {
        var cursor = NameCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);
        var behaviorKindId = request.BehaviorKindId is { } id ? new OperationBehaviorKindId(id) : (OperationBehaviorKindId?)null;

        var page = await repository.ListAsync(request.IncludeInactive, behaviorKindId, cursor, limit, cancellationToken);

        var items = page.Items.Select(OperationTypeDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new NameCursor(items[^1].Name, page.Items[^1].CreatedAt).Encode()
            : null;

        return new OperationTypePageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
