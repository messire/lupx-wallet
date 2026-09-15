using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Narrow read-only contract published by ReferenceData for ExchangeRates (W1.1, top-down
/// direction — ExchangeRates already references ReferenceData.Application, same as
/// Operations does for IOperationTypeLookup, ADR-0001) — resolves the currency code (e.g.
/// "USD") needed to call Frankfurter (ADR-0004 §4: codes, not Ids).
/// </summary>
public interface ICurrencyLookup
{
    Task<CurrencyLookupResult?> GetAsync(CurrencyId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<CurrencyLookupResult>> GetActiveAsync(CancellationToken cancellationToken);

    /// <summary>All catalog currencies, including deactivated ones, for exchange-rate refresh.</summary>
    Task<IReadOnlyList<CurrencyLookupResult>> GetAllAsync(CancellationToken cancellationToken);
}

public sealed record CurrencyLookupResult(CurrencyId Id, string Code, bool IsActive);
