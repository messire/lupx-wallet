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
        if (string.IsNullOrWhiteSpace(code))
        {
            return null;
        }

        // An active currency only needs the shared lock, so wallet creations in one currency do not serialise.
        // If the currency was removed while this transaction waited, it is reported as unknown: taking the
        // exclusive lock now, while still holding the shared one, could deadlock with another waiter.
        if (await policy.FindActiveAsync(code, cancellationToken) is not null)
        {
            await unitOfWork.LockCurrencyCodeSharedAsync(code, cancellationToken);
            return await policy.FindActiveAsync(code, cancellationToken) is { } active
                ? new CurrencyLookupResult(active.Id, active.Code)
                : null;
        }

        // Two wallet creations may activate the same code at once; without the lock the loser fails on the unique index.
        await unitOfWork.LockCurrencyCodeAsync(code, cancellationToken);

        var currency = await policy.EnsureActiveAsync(code, cancellationToken);
        if (currency is null)
        {
            return null;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new CurrencyLookupResult(currency.Id, currency.Code);
    }
}
