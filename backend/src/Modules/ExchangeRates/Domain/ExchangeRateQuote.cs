using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Domain;

/// <summary>
/// ExchangeRateQuote aggregate (ddd-model.md, §2.7) — the "latest known" rate for a
/// currency pair, identified by (FromCurrencyId, ToCurrencyId), no surrogate Id (like
/// BalanceHistory.Domain.BalanceSnapshot). Rates "as of a date" are a separate entity,
/// <see cref="HistoricalExchangeRate"/> (append-only, same conceptual aggregate boundary:
/// "one rate for one pair at one point/date").
///
/// Does not implement IHasDomainEvents/AggregateRoot&lt;TId&gt;: the RefreshExchangeRates
/// events (ExchangeRatesUpdated/ExchangeRateUpdateFailed) describe the result of a whole
/// refresh run — several pairs per call, some of which may fail without a DB row to carry
/// the failure event — so they are published directly from RefreshExchangeRatesCommandHandler
/// via IDomainEventDispatcher rather than through the usual "EF SaveChanges interceptor
/// reads DomainEvents off a tracked aggregate" path (see the comment in
/// ExchangeRates.Application/RefreshExchangeRatesCommand.cs).
/// </summary>
public sealed class ExchangeRateQuote
{
    private decimal _rate;

    public CurrencyId FromCurrencyId { get; private set; }
    public CurrencyId ToCurrencyId { get; private set; }
    public ExchangeRateValue Rate => new(_rate);
    public DateTimeOffset FetchedAt { get; private set; }

    private ExchangeRateQuote()
    {
        // Только для EF Core.
    }

    public static ExchangeRateQuote Create(CurrencyId from, CurrencyId to, ExchangeRateValue rate, DateTimeOffset fetchedAt)
    {
        EnsureDifferentCurrencies(from, to);
        return new ExchangeRateQuote
        {
            FromCurrencyId = from,
            ToCurrencyId = to,
            _rate = rate.Rate,
            FetchedAt = fetchedAt,
        };
    }

    /// <summary>
    /// Business requirements §2 / ddd-model.md §5: Rate and FetchedAt change only on a
    /// successful source response — on failure the caller (RefreshExchangeRatesCommand)
    /// simply does not call this method for that pair.
    /// </summary>
    public void UpdateRate(ExchangeRateValue rate, DateTimeOffset fetchedAt)
    {
        _rate = rate.Rate;
        FetchedAt = fetchedAt;
    }

    private static void EnsureDifferentCurrencies(CurrencyId from, CurrencyId to)
    {
        if (from == to)
        {
            throw new SameCurrencyExchangeRateException(from.Value);
        }
    }
}
