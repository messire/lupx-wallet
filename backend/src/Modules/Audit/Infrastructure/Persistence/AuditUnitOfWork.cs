using LupexWallet.Audit.Application;

namespace LupexWallet.Audit.Infrastructure;

public sealed class AuditUnitOfWork(AuditDbContext dbContext) : IAuditUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
