using LupexWallet.Operations.Domain;

namespace LupexWallet.Operations.Application;

public interface IOperationsUnitOfWork
{
    /// <summary>
    /// Persists the changes. Throws <see cref="RecordModifiedConcurrentlyException"/> when an
    /// operation/transfer was changed (deleted, restored, edited) by a concurrent request after
    /// it was loaded — the whole command must then roll back.
    /// </summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

/// <summary>An operation/transfer was modified by a concurrent request; answered with 409.</summary>
public sealed class RecordModifiedConcurrentlyException(Exception innerException)
    : OperationsDomainException("Запись изменена параллельно — обновите список и повторите действие.", innerException);
