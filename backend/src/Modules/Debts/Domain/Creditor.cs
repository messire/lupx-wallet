using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Domain;

/// <summary>
/// Module-owned creditor dictionary shared by debts and loans (ADR-0010 §3). Lifecycle per
/// ADR-0002 §4: delete if unused, otherwise deactivate.
/// </summary>
public sealed class Creditor : AggregateRoot<CreditorId>
{
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Creditor()
    {
        // EF Core only.
    }

    public static Creditor Create(CreditorId id, string name)
    {
        var now = DateTimeOffset.UtcNow;
        var creditor = new Creditor
        {
            Id = id,
            Name = RequireName(name),
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        creditor.Raise(new CreditorCreated(id));
        return creditor;
    }

    /// <summary>No-op when the name is unchanged.</summary>
    public void Rename(string name)
    {
        var trimmed = RequireName(name);
        if (trimmed == Name)
        {
            return;
        }

        Name = trimmed;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new CreditorRenamed(Id));
    }

    /// <summary>No-op when already inactive.</summary>
    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new CreditorDeactivated(Id));
    }

    public void EnsureCanBeDeleted(bool isUsed)
    {
        if (isUsed)
        {
            throw new CreditorInUseException();
        }
    }

    public void MarkAsDeleted() => Raise(new CreditorDeleted(Id));

    internal static string RequireName(string? name) =>
        string.IsNullOrWhiteSpace(name) ? throw new DebtsNameRequiredException() : name.Trim();
}
