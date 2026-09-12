namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Value object for a currency pair rate (ddd-model.md, §4) — a distinct type rather than
/// a bare decimal, to prevent accidental arithmetic on the rate as a unitless number.
/// Invariant: Rate &gt; 0 (ddd-model.md, §5, "ExchangeRateQuote" section).
/// </summary>
public readonly record struct ExchangeRateValue
{
    public decimal Rate { get; }

    public ExchangeRateValue(decimal rate)
    {
        if (rate <= 0)
        {
            throw new InvalidExchangeRateException(rate);
        }

        Rate = rate;
    }
}
