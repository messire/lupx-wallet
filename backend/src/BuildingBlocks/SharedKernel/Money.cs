namespace LupexWallet.SharedKernel;

/// <summary>
/// Value object combining an amount and a currency (ddd-model.md, §4). Uses arbitrary
/// decimal precision (ADR-0005) — rounding happens only at the presentation layer, not here.
/// </summary>
public readonly record struct Money(decimal Amount, CurrencyId CurrencyId)
{
    public static Money Zero(CurrencyId currencyId) => new(0m, currencyId);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount + other.Amount };
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return this with { Amount = Amount - other.Amount };
    }

    public Money Negate() => this with { Amount = -Amount };

    private void EnsureSameCurrency(Money other)
    {
        if (CurrencyId != other.CurrencyId)
        {
            throw new InvalidOperationException(
                $"Cannot operate on Money values with different currencies: {CurrencyId} vs {other.CurrencyId}.");
        }
    }
}
