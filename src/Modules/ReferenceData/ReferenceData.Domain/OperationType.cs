using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// Пользовательский расширяемый справочник типов операций (ddd-model.md, §2.2;
/// раздел 3 бизнес-требований). Ссылается на OperationBehaviorKind — этой ссылкой
/// определяется базовое поведение (доход/расход/перевод/корректировка).
/// </summary>
public sealed class OperationType : AggregateRoot<OperationTypeId>
{
    public string Name { get; private set; } = null!;

    /// <summary>Неизменяема после создания (ddd-model.md, §5) — смена поведения задним
    /// числом изменила бы смысл всех уже существующих операций этого типа.</summary>
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
