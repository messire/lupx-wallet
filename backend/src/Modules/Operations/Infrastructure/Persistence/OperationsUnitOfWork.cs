using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationsUnitOfWork(OperationsDbContext dbContext) : IOperationsUnitOfWork
{
    public Task AcquireWalletLocksAsync(IEnumerable<WalletId> walletIds, CancellationToken cancellationToken) =>
        WalletAdvisoryLock.AcquireAllAsync(dbContext.Database, walletIds, cancellationToken);

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
