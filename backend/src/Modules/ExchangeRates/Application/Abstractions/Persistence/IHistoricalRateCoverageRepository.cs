using LupexWallet.ExchangeRates.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.ExchangeRates.Application;

/// <summary>Coverage of the loaded rate history per pair (ADR-0004, §9).</summary>
public interface IHistoricalRateCoverageRepository
{
    void Add(HistoricalRateCoverage coverage);

    /// <summary>Tracked coverage row of the pair, or null if no history was loaded for it yet.</summary>
    Task<HistoricalRateCoverage?> FindAsync(CurrencyId from, CurrencyId to, CancellationToken cancellationToken);

    /// <summary>Bulk-deletes every coverage row where the currency is From or To (ADR-0002 §4, currency deletion); bypasses the ChangeTracker.</summary>
    Task RemoveAllForCurrencyAsync(CurrencyId currencyId, CancellationToken cancellationToken);

    /// <summary>Same contract as <see cref="IHistoricalExchangeRateRepository.RemoveStaleAsync"/>, for the coverage rows.</summary>
    Task RemoveStaleAsync(
        CurrencyId primaryCurrencyId, IReadOnlyCollection<CurrencyId> activeCurrencyIds, CancellationToken cancellationToken);
}
