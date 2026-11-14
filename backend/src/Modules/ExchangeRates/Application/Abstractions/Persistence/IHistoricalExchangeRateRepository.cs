using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

public interface IHistoricalExchangeRateRepository
{
    void Add(HistoricalExchangeRate rate);

    /// <summary>Tracked rows of the pair with RateDate in [<paramref name="fromDate"/>; <paramref name="toDate"/>] — the existing side of an upsert (ADR-0004, §9).</summary>
    Task<IReadOnlyList<HistoricalExchangeRate>> GetRangeAsync(
        CurrencyId from, CurrencyId to, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);

    /// <summary>
    /// Closest stored date not after <paramref name="onOrBefore"/> (ADR-0004, §8/§9: a date without
    /// a rate, e.g. a weekend, uses the closest earlier one). No tracking.
    /// </summary>
    Task<HistoricalExchangeRate?> FindClosestBeforeAsync(
        CurrencyId from, CurrencyId to, DateOnly onOrBefore, CancellationToken cancellationToken);

    /// <summary>Bulk-deletes every rate where the currency is From or To (ADR-0002 §4, currency deletion); bypasses the ChangeTracker.</summary>
    Task RemoveAllForCurrencyAsync(CurrencyId currencyId, CancellationToken cancellationToken);

    /// <summary>
    /// Bulk-deletes rates that the current primary currency makes useless: pairs not targeting
    /// <paramref name="primaryCurrencyId"/> and pairs whose source currency is not in
    /// <paramref name="activeCurrencyIds"/> (ADR-0004, §9). Bypasses the ChangeTracker.
    /// </summary>
    Task RemoveStaleAsync(
        CurrencyId primaryCurrencyId, IReadOnlyCollection<CurrencyId> activeCurrencyIds, CancellationToken cancellationToken);
}
