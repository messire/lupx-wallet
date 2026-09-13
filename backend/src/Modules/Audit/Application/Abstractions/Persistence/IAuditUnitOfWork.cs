namespace LupexWallet.Audit.Application;

public interface IAuditUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
