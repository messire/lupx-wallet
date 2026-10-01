using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Adds a currency to the active set from the available list (ADR-0004 §7). Shared by
/// <see cref="ActivateCurrencyCommand"/> and the <see cref="ICurrencyActivation"/> port used by
/// wallet creation; neither saves — the caller owns the unit of work.
/// </summary>
public sealed class CurrencyActivationPolicy(ICurrencyRepository currencies, IAvailableCurrencyRepository available)
{
    /// <summary>
    /// Throws <see cref="CurrencyCodeAlreadyExistsException"/> if the code is already active and
    /// <see cref="CurrencyNotAvailableException"/> if the rate service does not offer it.
    /// </summary>
    public async Task<Currency> ActivateAsync(string? code, CancellationToken cancellationToken)
    {
        var normalized = Normalize(code) ?? throw new CurrencyCodeRequiredException();

        if (await currencies.CodeExistsAsync(normalized, cancellationToken))
        {
            throw new CurrencyCodeAlreadyExistsException(normalized);
        }

        return await CreateFromAvailableAsync(normalized, cancellationToken)
            ?? throw new CurrencyNotAvailableException(normalized);
    }

    /// <summary>
    /// The active currency with this code; activated from the available list if needed.
    /// <c>null</c> if the code is neither active nor available. An active currency that the
    /// service no longer lists stays usable (ADR-0004 §6).
    /// </summary>
    public async Task<Currency?> EnsureActiveAsync(string? code, CancellationToken cancellationToken)
    {
        var normalized = Normalize(code);
        if (normalized is null)
        {
            return null;
        }

        return await currencies.GetByCodeAsync(normalized, cancellationToken)
            ?? await CreateFromAvailableAsync(normalized, cancellationToken);
    }

    private async Task<Currency?> CreateFromAvailableAsync(string normalizedCode, CancellationToken cancellationToken)
    {
        var offered = await available.GetByCodeAsync(normalizedCode, cancellationToken);
        if (offered is null)
        {
            return null;
        }

        var currency = Currency.Create(CurrencyId.New(), offered.Code, offered.Name);
        currencies.Add(currency);
        return currency;
    }

    private static string? Normalize(string? code) =>
        string.IsNullOrWhiteSpace(code) ? null : code.Trim().ToUpperInvariant();
}
