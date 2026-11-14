namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Port published by ReferenceData for Wallets (ADR-0001 §2, way c): wallet creation needs
/// the currency to be active, so activation is a synchronous call inside the command's
/// transaction, not an event (ADR-0001 §3; ADR-0004 §7).
/// </summary>
public interface ICurrencyActivation
{
    /// <summary>
    /// Returns the active currency with this code, activating it from the available list if it
    /// is not active yet (raises <c>CurrencyCreated</c>). Returns <c>null</c> if the code is
    /// neither active nor available. Saves through the module's own DbContext inside the
    /// caller's ambient transaction, so a rollback of the caller undoes the activation.
    /// </summary>
    Task<CurrencyLookupResult?> EnsureActiveAsync(string code, CancellationToken cancellationToken);
}
