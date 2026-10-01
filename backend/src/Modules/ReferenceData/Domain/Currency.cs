using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// Active currency (ADR-0004 §7): a row exists = the currency is active; removing it from the
/// active set is a delete (there is no deactivated state). Not restricted to ISO 4217 —
/// codes that the rate service does not know stay usable (decision Q14).
/// </summary>
public sealed class Currency : AggregateRoot<CurrencyId>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private Currency()
    {
        // Только для EF Core.
    }

    public static Currency Create(CurrencyId id, string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new CurrencyCodeRequiredException();
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReferenceItemNameRequiredException();
        }

        var now = DateTimeOffset.UtcNow;
        var currency = new Currency
        {
            Id = id,
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            CreatedAt = now,
            UpdatedAt = now,
        };

        currency.Raise(new CurrencyCreated(id));
        return currency;
    }

    /// <summary>Applies the service name (ADR-0004 §7); a no-op if the name is unchanged.</summary>
    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ReferenceItemNameRequiredException();
        }

        var trimmed = name.Trim();
        if (trimmed == Name)
        {
            return;
        }

        Name = trimmed;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new CurrencyRenamed(Id));
    }

    public void EnsureCanBeDeleted(bool isUsed)
    {
        if (isUsed)
        {
            throw new CurrencyInUseException(Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new CurrencyDeleted(Id));
}
