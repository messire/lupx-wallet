using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Currency pair rate "as of a date" (ddd-model.md, §2.7 — a second identity variant of the
/// same conceptual ExchangeRateQuote aggregate) — an append-only cache, populated as
/// Reporting actually requests a historical amount for a specific past date (requirements
/// §5, docs/database/schema.md: historical_exchange_rates). Identified by the triple
/// (FromCurrencyId, ToCurrencyId, RateDate) — no surrogate Id.
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
}
