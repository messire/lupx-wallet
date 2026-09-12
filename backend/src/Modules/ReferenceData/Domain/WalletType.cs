using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// User-extensible wallet type reference (ddd-model.md, §2.2; requirements §7). Lifecycle:
/// create → (use) → deactivate, or create → delete if the item is unused (ADR-0002).
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

    /// <summary>Pre-deletion check — the deletion decision itself is made by the Application layer.</summary>
    public void EnsureCanBeDeleted(bool isUsed)
    {
        if (isUsed)
        {
            throw new ReferenceItemInUseException(nameof(WalletType), Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new WalletTypeDeleted(Id));
}
