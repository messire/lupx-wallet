using LupexWallet.Budgeting.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Budgeting.Application;

public sealed record CategoryLimitDto(Guid ExpenseCategoryId, decimal Limit);

public sealed record BudgetDto(Guid Id, string Month, Guid CurrencyId, IReadOnlyList<CategoryLimitDto> Limits, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt)
{
    public static BudgetDto FromDomain(Budget budget) => new(
        budget.Id.Value,
        budget.BudgetMonth.ToString(),
        budget.CurrencyId.Value,
        budget.Limits.OrderBy(l => l.ExpenseCategoryId.Value).Select(l => new CategoryLimitDto(l.ExpenseCategoryId.Value, l.Limit)).ToList(),
        budget.CreatedAt,
        budget.UpdatedAt);
}

public sealed record BudgetPageDto(IReadOnlyList<BudgetDto> Data, string? NextCursor, bool HasMore);

// ---- Create ----
public sealed record CreateBudgetCommand(string Month, Guid CurrencyId) : IRequest<BudgetDto>, ICommand<BudgetDto>;

public sealed class CreateBudgetCommandHandler(
    IBudgetRepository repository,
    ICurrencyLookup currencyLookup,
    IBudgetingUnitOfWork unitOfWork) : IRequestHandler<CreateBudgetCommand, BudgetDto>
{
    public async Task<BudgetDto> Handle(CreateBudgetCommand request, CancellationToken cancellationToken)
    {
        var month = BudgetMonth.Parse(request.Month);
        var currencyId = new CurrencyId(request.CurrencyId);
        var currency = await currencyLookup.GetAsync(currencyId, cancellationToken)
            ?? throw new BudgetCurrencyNotActiveException(request.CurrencyId);

        if (await repository.ExistsAsync(month.FirstDay, currencyId, cancellationToken))
        {
            throw new BudgetAlreadyExistsException(month.ToString(), currency.Code);
        }

        var budget = Budget.Create(BudgetId.New(), month, currencyId);
        repository.Add(budget);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BudgetDto.FromDomain(budget);
    }
}

// ---- Set / remove a category limit ----
public sealed record SetBudgetCategoryLimitCommand(Guid BudgetId, Guid ExpenseCategoryId, decimal Limit)
    : IRequest<BudgetDto>, ICommand<BudgetDto>;

public sealed class SetBudgetCategoryLimitCommandHandler(
    IBudgetRepository repository,
    IExpenseCategoryRepository categoryRepository,
    IBudgetingUnitOfWork unitOfWork) : IRequestHandler<SetBudgetCategoryLimitCommand, BudgetDto>
{
    public async Task<BudgetDto> Handle(SetBudgetCategoryLimitCommand request, CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(new BudgetId(request.BudgetId), cancellationToken)
            ?? throw new KeyNotFoundException($"Budget {request.BudgetId} не найден.");

        var category = await categoryRepository.GetByIdAsync(new ExpenseCategoryId(request.ExpenseCategoryId), cancellationToken)
            ?? throw new ExpenseCategoryNotFoundException(request.ExpenseCategoryId);

        if (!category.IsActive)
        {
            throw new ExpenseCategoryInactiveException(category.Name);
        }

        budget.SetLimit(category.Id, request.Limit);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BudgetDto.FromDomain(budget);
    }
}

public sealed record RemoveBudgetCategoryLimitCommand(Guid BudgetId, Guid ExpenseCategoryId)
    : IRequest<BudgetDto>, ICommand<BudgetDto>;

public sealed class RemoveBudgetCategoryLimitCommandHandler(IBudgetRepository repository, IBudgetingUnitOfWork unitOfWork)
    : IRequestHandler<RemoveBudgetCategoryLimitCommand, BudgetDto>
{
    public async Task<BudgetDto> Handle(RemoveBudgetCategoryLimitCommand request, CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(new BudgetId(request.BudgetId), cancellationToken)
            ?? throw new KeyNotFoundException($"Budget {request.BudgetId} не найден.");

        if (!budget.RemoveLimit(new ExpenseCategoryId(request.ExpenseCategoryId)))
        {
            throw new KeyNotFoundException($"В бюджете нет лимита по категории {request.ExpenseCategoryId}.");
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return BudgetDto.FromDomain(budget);
    }
}

// ---- Delete (a budget holds no facts, so it is removed physically, ADR-0010 §8) ----
public sealed record DeleteBudgetCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteBudgetCommandHandler(IBudgetRepository repository, IBudgetingUnitOfWork unitOfWork)
    : IRequestHandler<DeleteBudgetCommand>
{
    public async Task Handle(DeleteBudgetCommand request, CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(new BudgetId(request.Id), cancellationToken)
            ?? throw new KeyNotFoundException($"Budget {request.Id} не найден.");

        budget.MarkAsDeleted();
        repository.Remove(budget);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}

// ---- Get / List ----
public sealed record GetBudgetQuery(Guid Id) : IRequest<BudgetDto?>, IQuery<BudgetDto?>;

public sealed class GetBudgetQueryHandler(IBudgetRepository repository) : IRequestHandler<GetBudgetQuery, BudgetDto?>
{
    public async Task<BudgetDto?> Handle(GetBudgetQuery request, CancellationToken cancellationToken)
    {
        var budget = await repository.GetByIdAsync(new BudgetId(request.Id), cancellationToken);
        return budget is null ? null : BudgetDto.FromDomain(budget);
    }
}

/// <param name="Month">Optional "YYYY-MM" filter.</param>
public sealed record ListBudgetsQuery(string? Month, string? Cursor, int Limit) : IRequest<BudgetPageDto>, IQuery<BudgetPageDto>;

public sealed class ListBudgetsQueryHandler(IBudgetRepository repository) : IRequestHandler<ListBudgetsQuery, BudgetPageDto>
{
    public async Task<BudgetPageDto> Handle(ListBudgetsQuery request, CancellationToken cancellationToken)
    {
        DateOnly? month = string.IsNullOrWhiteSpace(request.Month) ? null : BudgetMonth.Parse(request.Month).FirstDay;
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);
        var page = await repository.ListAsync(month, BudgetCursor.TryDecode(request.Cursor), limit, cancellationToken);

        var nextCursor = page.HasMore && page.Items.Count > 0
            ? new BudgetCursor(page.Items[^1].Month, page.Items[^1].CreatedAt).Encode()
            : null;
        return new BudgetPageDto(page.Items.Select(BudgetDto.FromDomain).ToList(), nextCursor, page.HasMore);
    }
}
