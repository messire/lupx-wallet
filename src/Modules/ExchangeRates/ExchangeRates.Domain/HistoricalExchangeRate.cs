using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// Курс валютной пары "на дату" (ddd-model.md, §2.7 — второй вариант идентичности того же
/// концептуального агрегата ExchangeRateQuote) — append-only кэш, пополняемый по мере того,
/// как Reporting реально запрашивает историческую сумму на конкретную прошедшую дату
/// (раздел 5 требований, docs/database/schema.md: historical_exchange_rates). Идентифицируется
/// тройкой (FromCurrencyId, ToCurrencyId, RateDate) — без суррогатного Id.
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
