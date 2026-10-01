using LupexWallet.Operations.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationsUnitOfWork(OperationsDbContext dbContext) : IOperationsUnitOfWork
{
    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new RecordModifiedConcurrentlyException(ex);
        }
    }
}
