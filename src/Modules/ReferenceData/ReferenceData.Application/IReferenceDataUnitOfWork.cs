namespace LupexWallet.ReferenceData.Application;

public interface IReferenceDataUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
