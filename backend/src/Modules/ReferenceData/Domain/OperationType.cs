using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// User-extensible operation type reference (ddd-model.md, §2.2; requirements §3). The
/// reference to OperationBehaviorKind determines the base behavior (income/expense/
/// transfer/adjustment).
/// </summary>
public sealed class OperationType : AggregateRoot<OperationTypeId>
{
    public string Name { get; private set; } = null!;

    /// <summary>Immutable after creation (ddd-model.md, §5) — changing it retroactively
    /// would change the meaning of all existing operations of this type.</summary>
    public OperationBehaviorKindId BehaviorKindId { get; private set; }

    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private OperationType()
    {
        // Только для EF Core.
    }

    public static OperationType Create(OperationTypeId id, string name, OperationBehaviorKindId behaviorKindId)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReferenceItemNameRequiredException();
        }

        var now = DateTimeOffset.UtcNow;
        var operationType = new OperationType
        {
            Id = id,
            Name = name.Trim(),
            BehaviorKindId = behaviorKindId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        operationType.Raise(new OperationTypeCreated(id));
        return operationType;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new OperationTypeDeactivated(Id));
    }

    public void EnsureCanBeDeleted(bool isUsed)
    {
        if (isUsed)
        {
            throw new ReferenceItemInUseException(nameof(OperationType), Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new OperationTypeDeleted(Id));
}
