namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// VO курса валютной пары (ddd-model.md, §4) — отдельный тип, а не decimal напрямую,
/// чтобы запретить случайную арифметику курса как обычного числа без единиц измерения.
/// Инвариант: Rate &gt; 0 (ddd-model.md, §5, раздел "ExchangeRateQuote").
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
