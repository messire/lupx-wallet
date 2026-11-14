using System.Globalization;
using LupexWallet.SharedKernel;

namespace LupexWallet.Budgeting.Domain;

/// <summary>
/// Monthly expense budget (ADR-0010 §5): month + currency, unique per pair, with category limits.
/// Reserves nothing and never touches wallet balances. No carry-over between months (B8).
/// </summary>
public sealed class Budget : AggregateRoot<BudgetId>
{
    private readonly List<CategoryLimit> _limits = [];

    /// <summary>First day of the budget month.</summary>
    public DateOnly Month { get; private set; }

    public CurrencyId CurrencyId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<CategoryLimit> Limits => _limits.AsReadOnly();

    private Budget()
    {
        // EF Core only.
    }

    public static Budget Create(BudgetId id, BudgetMonth month, CurrencyId currencyId)
    {
        var now = DateTimeOffset.UtcNow;
        var budget = new Budget
        {
            Id = id,
            Month = month.FirstDay,
            CurrencyId = currencyId,
            CreatedAt = now,
            UpdatedAt = now,
        };

        budget.Raise(new BudgetCreated(id));
        return budget;
    }

    public BudgetMonth BudgetMonth => BudgetMonth.FromDate(Month);

    /// <summary>Adds or changes a limit. Whether the category may be used is checked by the caller.</summary>
    public void SetLimit(ExpenseCategoryId categoryId, decimal limit)
    {
        if (limit < 0)
        {
            throw new BudgetLimitInvalidException();
        }

        var existing = _limits.FirstOrDefault(l => l.ExpenseCategoryId == categoryId);
        if (existing is not null && existing.Limit == limit)
        {
            return;
        }

        var oldLimit = existing?.Limit;
        if (existing is null)
        {
            _limits.Add(new CategoryLimit(categoryId, limit));
        }
        else
        {
            existing.Change(limit);
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new BudgetCategoryLimitSet(Id, categoryId, oldLimit, limit));
    }

    public bool HasLimit(ExpenseCategoryId categoryId) => _limits.Any(l => l.ExpenseCategoryId == categoryId);

    /// <summary>Returns false when the category has no limit in this budget.</summary>
    public bool RemoveLimit(ExpenseCategoryId categoryId)
    {
        var existing = _limits.FirstOrDefault(l => l.ExpenseCategoryId == categoryId);
        if (existing is null)
        {
            return false;
        }

        _limits.Remove(existing);
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new BudgetCategoryLimitRemoved(Id, categoryId, existing.Limit));
        return true;
    }

    public void MarkAsDeleted() =>
        Raise(new BudgetDeleted(Id, _limits.Select(l => new CategoryLimitSnapshot(l.ExpenseCategoryId, l.Limit)).ToList()));
}

/// <summary>Limit of one expense category inside a <see cref="Budget"/>.</summary>
public sealed class CategoryLimit
{
    public ExpenseCategoryId ExpenseCategoryId { get; private set; }

    /// <summary>In the budget currency; arbitrary precision (ADR-0005).</summary>
    public decimal Limit { get; private set; }

    private CategoryLimit()
    {
        // EF Core only.
    }

    internal CategoryLimit(ExpenseCategoryId categoryId, decimal limit)
    {
        ExpenseCategoryId = categoryId;
        Limit = limit;
    }

    internal void Change(decimal limit) => Limit = limit;
}

/// <summary>Calendar month "YYYY-MM".</summary>
public readonly record struct BudgetMonth(int Year, int Month)
{
    public DateOnly FirstDay => new(Year, Month, 1);

    public DateOnly LastDay => FirstDay.AddMonths(1).AddDays(-1);

    public static BudgetMonth FromDate(DateOnly date) => new(date.Year, date.Month);

    public static BudgetMonth Parse(string? value)
    {
        if (value is { Length: 7 } &&
            DateOnly.TryParseExact(value + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return FromDate(date);
        }

        throw new BudgetMonthInvalidException(value);
    }

    public override string ToString() => FirstDay.ToString("yyyy-MM", CultureInfo.InvariantCulture);
}
