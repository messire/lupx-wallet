namespace LupexWallet.ReferenceData.Application;

public interface IReferenceDataUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Exclusive lock for changing the active state of one currency code (activation, removal): waits until
    /// every other transaction that holds the lock for the same code, shared or exclusive, has ended, and keeps
    /// it until the ambient transaction ends (an advisory transaction lock; without a transaction it is
    /// released at once). After it the "is the code active" and usage checks see what the others committed, so
    /// a loser of an activation race reuses or reports the existing currency instead of failing on the unique
    /// index of the code (a failed statement aborts the transaction, so it could not recover inside a wallet
    /// command), and a removal cannot interleave with a wallet that is being created on the currency.
    /// </summary>
    Task LockCurrencyCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Shared form of <see cref="LockCurrencyCodeAsync"/> for a transaction that starts using an already active
    /// currency: such transactions do not block each other, but wait for a pending removal or activation and
    /// are waited for by it. Must not be taken before an exclusive lock on the same code in one transaction.
    /// </summary>
    Task LockCurrencyCodeSharedAsync(string code, CancellationToken cancellationToken);
}
