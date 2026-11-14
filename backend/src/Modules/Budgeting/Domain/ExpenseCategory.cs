using LupexWallet.SharedKernel;

namespace LupexWallet.Budgeting.Domain;

/// <summary>
/// User dictionary of expense categories (ADR-0010 §5, decision B1): a name plus the operation
/// types with Expense behavior that belong to it. A type belongs to at most one category — a
/// collection invariant checked by the application layer and a unique index. Lifecycle follows
/// ADR-0002 §4: deactivate anytime, delete only when no budget uses it.
/// </summary>
public sealed class ExpenseCategory : AggregateRoot<ExpenseCategoryId>
{
    public const int NameMaxLength = 200;

    private readonly List<ExpenseCategoryMember> _members = [];

    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public IReadOnlyCollection<ExpenseCategoryMember> Members => _members.AsReadOnly();

    public IReadOnlyList<OperationTypeId> OperationTypeIds =>
        _members.Select(m => m.OperationTypeId).OrderBy(id => id.Value).ToList();

    private ExpenseCategory()
    {
        // EF Core only.
    }

    public static ExpenseCategory Create(ExpenseCategoryId id, string name, IEnumerable<OperationTypeId> operationTypeIds)
    {
        var now = DateTimeOffset.UtcNow;
        var category = new ExpenseCategory
        {
            Id = id,
            Name = NormalizeName(name),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        foreach (var typeId in operationTypeIds.Distinct())
        {
            category._members.Add(new ExpenseCategoryMember(typeId));
        }

        category.Raise(new ExpenseCategoryCreated(id, category.OperationTypeIds));
        return category;
    }

    /// <summary>Renames and/or replaces the member set; null leaves the part unchanged. No-op raises nothing.</summary>
    public void Update(string? name, IEnumerable<OperationTypeId>? operationTypeIds)
    {
        var nameChanged = false;
        if (name is not null)
        {
            var normalized = NormalizeName(name);
            nameChanged = normalized != Name;
            Name = normalized;
        }

        IReadOnlyList<OperationTypeId>? oldIds = null;
        IReadOnlyList<OperationTypeId>? newIds = null;
        if (operationTypeIds is not null)
        {
            var target = operationTypeIds.ToHashSet();
            var current = _members.Select(m => m.OperationTypeId).ToHashSet();
            if (!target.SetEquals(current))
            {
                oldIds = OperationTypeIds;
                // Diff instead of clear+add: re-adding the same key would collide in the ChangeTracker.
                _members.RemoveAll(m => !target.Contains(m.OperationTypeId));
                foreach (var typeId in target.Where(t => !current.Contains(t)))
                {
                    _members.Add(new ExpenseCategoryMember(typeId));
                }

                newIds = OperationTypeIds;
            }
        }

        if (!nameChanged && oldIds is null)
        {
            return;
        }

        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new ExpenseCategoryUpdated(Id, oldIds, newIds));
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new ExpenseCategoryDeactivated(Id));
    }

    public void EnsureCanBeDeleted(bool isUsedByBudget)
    {
        if (isUsedByBudget)
        {
            throw new ExpenseCategoryInUseException(Name);
        }
    }

    public void MarkAsDeleted() => Raise(new ExpenseCategoryDeleted(Id, OperationTypeIds));

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ExpenseCategoryNameRequiredException();
        }

        var trimmed = name.Trim();
        if (trimmed.Length > NameMaxLength)
        {
            throw new ExpenseCategoryNameTooLongException(NameMaxLength);
        }

        return trimmed;
    }
}

/// <summary>One operation type that belongs to an <see cref="ExpenseCategory"/>.</summary>
public sealed class ExpenseCategoryMember
{
    public OperationTypeId OperationTypeId { get; private set; }

    private ExpenseCategoryMember()
    {
        // EF Core only.
    }

    internal ExpenseCategoryMember(OperationTypeId operationTypeId) => OperationTypeId = operationTypeId;
}
