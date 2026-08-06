using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Domain;

/// <summary>
/// Пользовательский расширяемый справочник валют (ddd-model.md, §2.2) — не жестко
/// ограничен ISO 4217, чтобы поддерживать в т.ч. криптовалюты (решение по Q14).
/// </summary>
public sealed class Currency : AggregateRoot<CurrencyId>
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public bool IsActive { get; private set; }
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
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };

        currency.Raise(new CurrencyCreated(id));
        return currency;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTimeOffset.UtcNow;
        Raise(new CurrencyDeactivated(Id));
    }

    public void EnsureCanBeDeleted(bool isUsed)
    {
        if (isUsed)
        {
            throw new ReferenceItemInUseException(nameof(Currency), Id.Value);
        }
    }

    public void MarkAsDeleted() => Raise(new CurrencyDeleted(Id));
}
