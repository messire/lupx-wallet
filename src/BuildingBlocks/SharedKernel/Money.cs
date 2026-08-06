namespace LupexWallet.SharedKernel;

/// <summary>
/// Value object «сумма + валюта» (ddd-model.md, §4). Произвольная точность decimal
/// (ADR-0005) — округление только на уровне отображения, не здесь.
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
