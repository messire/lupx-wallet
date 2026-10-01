using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Narrow read-only contract published by ReferenceData for ExchangeRates and Wallets
/// (top-down direction, ADR-0001 §2) — resolves the currency code (e.g. "USD") needed to call
/// Frankfurter (ADR-0004 §4: codes, not Ids). Only active currencies exist (ADR-0004 §7).
/// </summary>
public interface ICurrencyLookup
{
    Task<CurrencyLookupResult?> GetAsync(CurrencyId id, CancellationToken cancellationToken);

    /// <summary>All active currencies.</summary>
    Task<IReadOnlyList<CurrencyLookupResult>> GetAllAsync(CancellationToken cancellationToken);
}

public sealed record CurrencyLookupResult(CurrencyId Id, string Code);
