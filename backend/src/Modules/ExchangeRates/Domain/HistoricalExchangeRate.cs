using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Currency pair rate "as of a date" (ddd-model.md, §2.7 — a second identity variant of the
/// same conceptual ExchangeRateQuote aggregate): loaded history, ADR-0004 §9. The background
/// history load fills it for [earliest accounting start date; today] and re-loads the right edge
/// (a day's rate can be refined after publication), hence <see cref="UpdateRate"/>; reads never
/// write it (ADR-0004 §8). Identified by (FromCurrencyId, ToCurrencyId, RateDate) — no surrogate Id.
/// </summary>
public sealed class HistoricalExchangeRate
{
    private decimal _rate;

    public CurrencyId FromCurrencyId { get; private set; }
    public CurrencyId ToCurrencyId { get; private set; }
    public DateOnly RateDate { get; private set; }
    public ExchangeRateValue Rate => new(_rate);
    public DateTimeOffset FetchedAt { get; private set; }

    private HistoricalExchangeRate()
    {
        // Только для EF Core.
    }

    public static HistoricalExchangeRate Create(
        CurrencyId from, CurrencyId to, DateOnly rateDate, ExchangeRateValue rate, DateTimeOffset fetchedAt)
    {
        if (from == to)
        {
            throw new SameCurrencyExchangeRateException(from.Value);
        }

        return new HistoricalExchangeRate
        {
            FromCurrencyId = from,
            ToCurrencyId = to,
            RateDate = rateDate,
            _rate = rate.Rate,
            FetchedAt = fetchedAt,
        };
    }

    /// <summary>Replaces the rate of an already stored date on a repeated load (ADR-0004, §9).</summary>
    public void UpdateRate(ExchangeRateValue rate, DateTimeOffset fetchedAt)
    {
        _rate = rate.Rate;
        FetchedAt = fetchedAt;
    }
}
