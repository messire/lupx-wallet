using LupexWallet.Budgeting.Application;
using LupexWallet.Budgeting.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Budgeting.Infrastructure;

public sealed class ExpenseCategoryRepository(BudgetingDbContext dbContext) : IExpenseCategoryRepository
{
    public void Add(ExpenseCategory category) => dbContext.ExpenseCategories.Add(category);

    public void Remove(ExpenseCategory category) => dbContext.ExpenseCategories.Remove(category);

    public Task<ExpenseCategory?> GetByIdAsync(ExpenseCategoryId id, CancellationToken cancellationToken) =>
        dbContext.ExpenseCategories.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.ExpenseCategories.ToListAsync(cancellationToken);

    public Task<bool> ActiveNameExistsAsync(string name, ExpenseCategoryId? exceptId, CancellationToken cancellationToken)
    {
        var lowered = name.ToLower();
        var query = dbContext.ExpenseCategories.Where(x => x.IsActive && x.Name.ToLower() == lowered);
        if (exceptId is { } id)
        {
            query = query.Where(x => x.Id != id);
        }

        return query.AnyAsync(cancellationToken);
    }

    public async Task<PageResult<ExpenseCategory>> ListAsync(
        bool includeInactive, CategoryCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.ExpenseCategories.AsQueryable();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (afterCursor is not null)
        {
            var cursorName = afterCursor.Name;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(x =>
                string.Compare(x.Name, cursorName) > 0 ||
                (x.Name == cursorName && x.CreatedAt > cursorCreatedAt));
        }

        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.CreatedAt).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new PageResult<ExpenseCategory>(items, hasMore);
    }
}

public sealed class BudgetRepository(BudgetingDbContext dbContext) : IBudgetRepository
{
    public void Add(Budget budget) => dbContext.Budgets.Add(budget);

    public void Remove(Budget budget) => dbContext.Budgets.Remove(budget);

    public Task<Budget?> GetByIdAsync(BudgetId id, CancellationToken cancellationToken) =>
        dbContext.Budgets.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(DateOnly month, CurrencyId currencyId, CancellationToken cancellationToken) =>
        dbContext.Budgets.AnyAsync(x => x.Month == month && x.CurrencyId == currencyId, cancellationToken);

    public Task<bool> AnyUsesCategoryAsync(ExpenseCategoryId categoryId, CancellationToken cancellationToken) =>
        dbContext.Budgets.AnyAsync(b => b.Limits.Any(l => l.ExpenseCategoryId == categoryId), cancellationToken);

    public async Task<PageResult<Budget>> ListAsync(DateOnly? month, BudgetCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Budgets.AsQueryable();
        if (month is { } m)
        {
            query = query.Where(x => x.Month == m);
        }

        if (afterCursor is not null)
        {
            var cursorMonth = afterCursor.Month;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(x => x.Month < cursorMonth || (x.Month == cursorMonth && x.CreatedAt > cursorCreatedAt));
        }

        var items = await query.OrderByDescending(x => x.Month).ThenBy(x => x.CreatedAt).Take(limit + 1).ToListAsync(cancellationToken);
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new PageResult<Budget>(items, hasMore);
    }
}
