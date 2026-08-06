using LupexWallet.Operations.Application;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationsUnitOfWork(OperationsDbContext dbContext) : IOperationsUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
