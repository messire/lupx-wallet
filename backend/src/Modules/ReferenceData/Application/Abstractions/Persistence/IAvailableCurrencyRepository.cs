using LupexWallet.ReferenceData.Domain;

namespace LupexWallet.ReferenceData.Application;

/// <summary>Read access to the list of currencies offered by the rate service (ADR-0004 §7).</summary>
public interface IAvailableCurrencyRepository
{
    Task<AvailableCurrency?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>The whole list ordered by code; <c>IsActive</c> = a currency with this code exists.</summary>
    Task<IReadOnlyList<AvailableCurrencyDto>> ListAsync(CancellationToken cancellationToken);
}
