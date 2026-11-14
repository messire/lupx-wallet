using LupexWallet.SharedKernel;

namespace LupexWallet.Budgeting.Domain;

// Budgeting events (ADR-0010 §5, §8). Child collections (member types, limits) are not in the
// ChangeTracker diff of the root, so the events carry them for the audit record.

public sealed record ExpenseCategoryCreated(ExpenseCategoryId ExpenseCategoryId, IReadOnlyList<OperationTypeId> OperationTypeIds) : DomainEvent;

/// <param name="OldOperationTypeIds">null when the member set did not change.</param>
public sealed record ExpenseCategoryUpdated(
    ExpenseCategoryId ExpenseCategoryId,
    IReadOnlyList<OperationTypeId>? OldOperationTypeIds,
    IReadOnlyList<OperationTypeId>? NewOperationTypeIds) : DomainEvent;

public sealed record ExpenseCategoryDeactivated(ExpenseCategoryId ExpenseCategoryId) : DomainEvent;

public sealed record ExpenseCategoryDeleted(ExpenseCategoryId ExpenseCategoryId, IReadOnlyList<OperationTypeId> OperationTypeIds) : DomainEvent;

public sealed record BudgetCreated(BudgetId BudgetId) : DomainEvent;

/// <param name="OldLimit">null when the category had no limit before.</param>
public sealed record BudgetCategoryLimitSet(BudgetId BudgetId, ExpenseCategoryId ExpenseCategoryId, decimal? OldLimit, decimal NewLimit) : DomainEvent;

public sealed record BudgetCategoryLimitRemoved(BudgetId BudgetId, ExpenseCategoryId ExpenseCategoryId, decimal OldLimit) : DomainEvent;

public sealed record BudgetDeleted(BudgetId BudgetId, IReadOnlyList<CategoryLimitSnapshot> Limits) : DomainEvent;

public sealed record CategoryLimitSnapshot(ExpenseCategoryId ExpenseCategoryId, decimal Limit);
