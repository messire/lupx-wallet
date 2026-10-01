using LupexWallet.ReferenceData.Application;

namespace LupexWallet.ReferenceData.Infrastructure;

/// <summary>
/// Implements <see cref="ICurrencyActivation"/>. Saves through <see cref="ReferenceDataDbContext"/>
/// inside the caller's ambient transaction, so the activation commits or rolls back with the
/// wallet command.
/// </summary>
public sealed class CurrencyActivation(
    CurrencyActivationPolicy policy,
    IReferenceDataUnitOfWork unitOfWork) : ICurrencyActivation
{
    public async Task<CurrencyLookupResult?> EnsureActiveAsync(string code, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(code))
        {
            // Two wallet creations may activate the same code at once; without the lock the loser fails on the unique index.
            await unitOfWork.LockCurrencyCodeAsync(code, cancellationToken);
        }

        var currency = await policy.EnsureActiveAsync(code, cancellationToken);
        if (currency is null)
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CurrencyLookupResult(currency.Id, currency.Code);
    }
}
