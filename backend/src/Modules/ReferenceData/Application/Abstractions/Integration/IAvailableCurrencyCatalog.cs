namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Port published by ReferenceData for ExchangeRates (ADR-0001 §2, way c — the owner of the
/// data exposes a write contract). ExchangeRates loads the currency list from the rate
/// service and hands it over; ReferenceData owns the storage
/// (<c>reference_data.available_currencies</c>, ADR-0004 §7).
/// </summary>
public interface IAvailableCurrencyCatalog
{
    /// <summary>
    /// Replaces the available list in full: new codes are added, names of existing ones are
    /// updated, codes missing from <paramref name="items"/> are removed. Active currencies whose
    /// code is present are renamed to the service name in the same unit of work. The caller
    /// must not pass an empty list (a failed load leaves the stored list untouched). Saves
    /// through the module's own DbContext inside the caller's ambient transaction, if any.
    /// </summary>
    Task ReplaceAsync(IReadOnlyList<AvailableCurrencyItem> items, CancellationToken cancellationToken);
}

public sealed record AvailableCurrencyItem(string Code, string Name);
