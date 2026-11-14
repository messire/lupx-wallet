using LupexWallet.Budgeting.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Budgeting.Application;

public sealed record ExpenseCategoryDto(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> OperationTypeIds)
{
    public static ExpenseCategoryDto FromDomain(ExpenseCategory entity) =>
        new(entity.Id.Value, entity.Name, entity.IsActive, entity.OperationTypeIds.Select(x => x.Value).ToList());
}

public sealed record ExpenseCategoryPageDto(IReadOnlyList<ExpenseCategoryDto> Data, string? NextCursor, bool HasMore);

/// <summary>
/// Checks the operation types of a category (ADR-0010 §5): each exists, has Expense behavior and is in
/// no other category. A type newly added must be active; a member kept as is may have been deactivated.
/// </summary>
public sealed class ExpenseCategoryMembershipPolicy(IOperationTypeLookup operationTypeLookup, IExpenseCategoryRepository repository)
{
    /// <summary>Behavior code from the stable system dictionary (requirements §3).</summary>
    public const string ExpenseBehaviorCode = "Expense";

    public async Task<IReadOnlyList<OperationTypeId>> ValidateAsync(
        ExpenseCategory? category, IReadOnlyCollection<Guid> operationTypeIds, CancellationToken cancellationToken)
    {
        var ids = operationTypeIds.Distinct().Select(x => new OperationTypeId(x)).ToList();
        var existingMembers = category?.OperationTypeIds.ToHashSet() ?? [];
        var others = (await repository.GetAllAsync(cancellationToken))
            .Where(c => category is null || c.Id != category.Id)
            .ToList();

        foreach (var id in ids)
        {
            var type = await operationTypeLookup.GetAsync(id, cancellationToken)
                ?? throw new OperationTypeNotFoundForCategoryException(id.Value);

            if (type.BehaviorKindCode != ExpenseBehaviorCode)
            {
                throw new OperationTypeNotExpenseException(type.Name);
            }

            if (!type.IsActive && !existingMembers.Contains(id))
            {
                throw new OperationTypeInactiveForCategoryException(type.Name);
            }

            var owner = others.FirstOrDefault(c => c.OperationTypeIds.Contains(id));
            if (owner is not null)
            {
                throw new OperationTypeAlreadyInCategoryException(type.Name, owner.Name);
            }
        }

        return ids;
    }
}

// ---- Create ----
public sealed record CreateExpenseCategoryCommand(string Name, IReadOnlyList<Guid> OperationTypeIds)
    : IRequest<ExpenseCategoryDto>, ICommand<ExpenseCategoryDto>;

public sealed class CreateExpenseCategoryCommandHandler(
    IExpenseCategoryRepository repository,
    ExpenseCategoryMembershipPolicy membershipPolicy,
    IBudgetingUnitOfWork unitOfWork) : IRequestHandler<CreateExpenseCategoryCommand, ExpenseCategoryDto>
{
    public async Task<ExpenseCategoryDto> Handle(CreateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        var typeIds = await membershipPolicy.ValidateAsync(null, request.OperationTypeIds ?? [], cancellationToken);
        var category = ExpenseCategory.Create(ExpenseCategoryId.New(), request.Name, typeIds);

        if (await repository.ActiveNameExistsAsync(category.Name, null, cancellationToken))
        {
            throw new ExpenseCategoryNameAlreadyExistsException(category.Name);
        }

        repository.Add(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExpenseCategoryDto.FromDomain(category);
    }
}

// ---- Update (rename and/or change member types) ----
public sealed record UpdateExpenseCategoryCommand(Guid Id, string? Name, IReadOnlyList<Guid>? OperationTypeIds)
    : IRequest<ExpenseCategoryDto>, ICommand<ExpenseCategoryDto>;

public sealed class UpdateExpenseCategoryCommandHandler(
    IExpenseCategoryRepository repository,
    ExpenseCategoryMembershipPolicy membershipPolicy,
    IBudgetingUnitOfWork unitOfWork) : IRequestHandler<UpdateExpenseCategoryCommand, ExpenseCategoryDto>
{
    public async Task<ExpenseCategoryDto> Handle(UpdateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(new ExpenseCategoryId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"ExpenseCategory {request.Id} не найдена.");

        IReadOnlyList<OperationTypeId>? typeIds = null;
        if (request.OperationTypeIds is not null)
        {
            typeIds = await membershipPolicy.ValidateAsync(category, request.OperationTypeIds, cancellationToken);
        }

        category.Update(request.Name, typeIds);

        if (category.IsActive && await repository.ActiveNameExistsAsync(category.Name, category.Id, cancellationToken))
        {
            throw new ExpenseCategoryNameAlreadyExistsException(category.Name);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExpenseCategoryDto.FromDomain(category);
    }
}

// ---- Deactivate ----
public sealed record DeactivateExpenseCategoryCommand(Guid Id) : IRequest<ExpenseCategoryDto>, ICommand<ExpenseCategoryDto>;

public sealed class DeactivateExpenseCategoryCommandHandler(IExpenseCategoryRepository repository, IBudgetingUnitOfWork unitOfWork)
    : IRequestHandler<DeactivateExpenseCategoryCommand, ExpenseCategoryDto>
{
    public async Task<ExpenseCategoryDto> Handle(DeactivateExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(new ExpenseCategoryId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"ExpenseCategory {request.Id} не найдена.");

        category.Deactivate();
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return ExpenseCategoryDto.FromDomain(category);
    }
}

// ---- Delete (only when no budget uses it, ADR-0002 §4) ----
public sealed record DeleteExpenseCategoryCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteExpenseCategoryCommandHandler(
    IExpenseCategoryRepository repository,
    IBudgetRepository budgetRepository,
    IBudgetingUnitOfWork unitOfWork) : IRequestHandler<DeleteExpenseCategoryCommand>
{
    public async Task Handle(DeleteExpenseCategoryCommand request, CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(new ExpenseCategoryId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"ExpenseCategory {request.Id} не найдена.");

        category.EnsureCanBeDeleted(await budgetRepository.AnyUsesCategoryAsync(category.Id, cancellationToken));
        category.MarkAsDeleted();
        repository.Remove(category);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- Get / List ----
public sealed record GetExpenseCategoryQuery(Guid Id) : IRequest<ExpenseCategoryDto?>, IQuery<ExpenseCategoryDto?>;

public sealed class GetExpenseCategoryQueryHandler(IExpenseCategoryRepository repository)
    : IRequestHandler<GetExpenseCategoryQuery, ExpenseCategoryDto?>
{
    public async Task<ExpenseCategoryDto?> Handle(GetExpenseCategoryQuery request, CancellationToken cancellationToken)
    {
        var category = await repository.GetByIdAsync(new ExpenseCategoryId(request.Id), cancellationToken);
        return category is null ? null : ExpenseCategoryDto.FromDomain(category);
    }
}

public sealed record ListExpenseCategoriesQuery(bool IncludeInactive, string? Cursor, int Limit)
    : IRequest<ExpenseCategoryPageDto>, IQuery<ExpenseCategoryPageDto>;

public sealed class ListExpenseCategoriesQueryHandler(IExpenseCategoryRepository repository)
    : IRequestHandler<ListExpenseCategoriesQuery, ExpenseCategoryPageDto>
{
    public async Task<ExpenseCategoryPageDto> Handle(ListExpenseCategoriesQuery request, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);
        var page = await repository.ListAsync(request.IncludeInactive, CategoryCursor.TryDecode(request.Cursor), limit, cancellationToken);

        var nextCursor = page.HasMore && page.Items.Count > 0
            ? new CategoryCursor(page.Items[^1].Name, page.Items[^1].CreatedAt).Encode()
            : null;
        return new ExpenseCategoryPageDto(page.Items.Select(ExpenseCategoryDto.FromDomain).ToList(), nextCursor, page.HasMore);
    }
}
