using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: CorrectionDateIndex })
        {
            // Two statements of the balance for the same wallet date raced past the wallet lock.
            throw new RecordModifiedConcurrentlyException(ex);
        }
    }

    private const string CorrectionDateIndex = "ux_operations_wallet_correction_date";
}
