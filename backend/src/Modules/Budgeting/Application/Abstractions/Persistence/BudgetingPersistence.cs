using LupexWallet.Budgeting.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Budgeting.Application;

public sealed record PageResult<T>(IReadOnlyList<T> Items, bool HasMore);

public interface IExpenseCategoryRepository
{
    void Add(ExpenseCategory category);
    void Remove(ExpenseCategory category);
    Task<ExpenseCategory?> GetByIdAsync(ExpenseCategoryId id, CancellationToken cancellationToken);

    /// <summary>All categories (active and inactive) with members — the dictionary is small.</summary>
    Task<IReadOnlyList<ExpenseCategory>> GetAllAsync(CancellationToken cancellationToken);

    Task<bool> ActiveNameExistsAsync(string name, ExpenseCategoryId? exceptId, CancellationToken cancellationToken);

    Task<PageResult<ExpenseCategory>> ListAsync(bool includeInactive, CategoryCursor? afterCursor, int limit, CancellationToken cancellationToken);
}

public interface IBudgetRepository
{
    void Add(Budget budget);
    void Remove(Budget budget);
    Task<Budget?> GetByIdAsync(BudgetId id, CancellationToken cancellationToken);
    Task<bool> ExistsAsync(DateOnly month, CurrencyId currencyId, CancellationToken cancellationToken);
    Task<bool> AnyUsesCategoryAsync(ExpenseCategoryId categoryId, CancellationToken cancellationToken);

    /// <summary>Ordered by month descending, then creation time.</summary>
    Task<PageResult<Budget>> ListAsync(DateOnly? month, BudgetCursor? afterCursor, int limit, CancellationToken cancellationToken);
}

public interface IBudgetingUnitOfWork
{
    /// <summary>Translates unique-index violations into domain conflicts.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
