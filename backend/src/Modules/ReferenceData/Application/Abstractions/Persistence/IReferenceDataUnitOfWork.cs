namespace LupexWallet.ReferenceData.Application;

public interface IReferenceDataUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Serialises concurrent activations of one currency code: waits until every other transaction that
    /// holds the lock for the same code has ended, and keeps it until the ambient transaction ends (an
    /// advisory transaction lock; without a transaction it is released at once). After it the "is the code
    /// active" check sees what the winner committed, so a loser reuses or reports the existing currency
    /// instead of failing on the unique index of the code (a failed statement aborts the transaction, so
    /// the loser could not recover from it inside a wallet command).
    /// </summary>
    Task LockCurrencyCodeAsync(string code, CancellationToken cancellationToken);
}
