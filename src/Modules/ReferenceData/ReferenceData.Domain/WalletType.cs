using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// Пользовательский расширяемый справочник типов кошельков (ddd-model.md, §2.2;
/// раздел 7 бизнес-требований). Жизненный цикл: создание → (использование) →
/// деактивация, либо создание → удаление, если элемент не использован (ADR-0002).
/// </summary>
public sealed class WalletType : AggregateRoot<WalletTypeId>
{
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private WalletType()
    {
        // Только для EF Core.
    }

    public static WalletType Create(WalletTypeId id, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReferenceItemNameRequiredException();
        }

        var now = DateTimeOffset.UtcNow;
        var walletType = new WalletType
        {
            Id = id,
            Name = name.Trim(),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        walletType.Raise(new WalletTypeCreated(id));
        return walletType;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new WalletTypeDeactivated(Id));
    }

    /// <summary>Проверка перед физическим удалением — само решение принимает Application-слой.</summary>
    public void EnsureCanBeDeleted(bool isUsed)
    {
        if (isUsed)
        {
            throw new ReferenceItemInUseException(nameof(WalletType), Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new WalletTypeDeleted(Id));
}
