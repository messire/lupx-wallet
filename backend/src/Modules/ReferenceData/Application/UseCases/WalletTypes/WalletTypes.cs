using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.ReferenceData.Application;

public sealed record WalletTypeDto(Guid Id, string Name, bool IsActive)
{
    public static WalletTypeDto FromDomain(WalletType entity) => new(entity.Id.Value, entity.Name, entity.IsActive);
}

public sealed record WalletTypePageDto(IReadOnlyList<WalletTypeDto> Data, string? NextCursor, bool HasMore);

public sealed record WalletTypePageResult(IReadOnlyList<WalletType> Items, bool HasMore);

public interface IWalletTypeRepository
{
    void Add(WalletType walletType);
    void Remove(WalletType walletType);
    Task<WalletType?> GetByIdAsync(WalletTypeId id, CancellationToken cancellationToken);
    Task<WalletTypePageResult> ListAsync(bool includeInactive, NameCursor? afterCursor, int limit, CancellationToken cancellationToken);
}

// ---- UC-28: создать тип кошелька ----
public sealed record CreateWalletTypeCommand(string Name) : IRequest<WalletTypeDto>, ICommand<WalletTypeDto>;

public sealed class CreateWalletTypeCommandHandler(IWalletTypeRepository repository, IReferenceDataUnitOfWork unitOfWork)
    : IRequestHandler<CreateWalletTypeCommand, WalletTypeDto>
{
    public async Task<WalletTypeDto> Handle(CreateWalletTypeCommand request, CancellationToken cancellationToken)
    {
        var walletType = WalletType.Create(WalletTypeId.New(), request.Name);
        repository.Add(walletType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletTypeDto.FromDomain(walletType);
    }
}

// ---- Деактивация ----
public sealed record DeactivateWalletTypeCommand(Guid Id) : IRequest<WalletTypeDto>, ICommand<WalletTypeDto>;

public sealed class DeactivateWalletTypeCommandHandler(IWalletTypeRepository repository, IReferenceDataUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateWalletTypeCommand, WalletTypeDto>
{
    public async Task<WalletTypeDto> Handle(DeactivateWalletTypeCommand request, CancellationToken cancellationToken)
    {
        var walletType = await repository.GetByIdAsync(new WalletTypeId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"WalletType {request.Id} не найден.");

        walletType.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return WalletTypeDto.FromDomain(walletType);
    }
}

// ---- Удаление (только если не используется — решено, Q11; см. ADR-0002) ----
public sealed record DeleteWalletTypeCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteWalletTypeCommandHandler(
    IWalletTypeRepository repository,
    IReferenceDataUnitOfWork unitOfWork,
    IEnumerable<IReferenceItemUsageProbe> usageProbes) : IRequestHandler<DeleteWalletTypeCommand>
{
    public async Task Handle(DeleteWalletTypeCommand request, CancellationToken cancellationToken)
    {
        var walletType = await repository.GetByIdAsync(new WalletTypeId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"WalletType {request.Id} не найден.");

        // ADR-0009, случай c: реальная проверка использования через реализации Wallets.
        var isUsed = await usageProbes.AnyIsUsedAsync(ReferenceItemKind.WalletType, request.Id, cancellationToken);
        walletType.EnsureCanBeDeleted(isUsed);

        walletType.MarkAsDeleted();
        repository.Remove(walletType);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- UC-26: список типов кошельков ----
public sealed record ListWalletTypesQuery(bool IncludeInactive, string? Cursor, int Limit)
    : IRequest<WalletTypePageDto>, IQuery<WalletTypePageDto>;

public sealed class ListWalletTypesQueryHandler(IWalletTypeRepository repository)
    : IRequestHandler<ListWalletTypesQuery, WalletTypePageDto>
{
    public async Task<WalletTypePageDto> Handle(ListWalletTypesQuery request, CancellationToken cancellationToken)
    {
        var cursor = NameCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(request.IncludeInactive, cursor, limit, cancellationToken);

        var items = page.Items.Select(WalletTypeDto.FromDomain).ToList();
        var lastCreatedAt = page.Items.Count > 0 ? GetCreatedAt(page.Items[^1]) : (DateTimeOffset?)null;
        var nextCursor = lastCreatedAt is not null
            ? new NameCursor(items[^1].Name, lastCreatedAt.Value).Encode()
            : null;

        return new WalletTypePageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }

    private static DateTimeOffset GetCreatedAt(WalletType entity) => entity.CreatedAt;
}
