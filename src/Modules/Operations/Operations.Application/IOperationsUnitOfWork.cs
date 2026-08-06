namespace LupexWallet.Operations.Application;

public interface IOperationsUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
