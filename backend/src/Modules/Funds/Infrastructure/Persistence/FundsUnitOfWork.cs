using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LupexWallet.Funds.Infrastructure;

public sealed class FundsUnitOfWork(FundsDbContext dbContext) : IFundsUnitOfWork
{
    public Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken) =>
        WalletAdvisoryLock.AcquireAsync(dbContext.Database, walletId, cancellationToken);

    public async Task SaveChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new FundModifiedConcurrentlyException(ex);
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: AllocationIndex })
        {
            throw new FundModifiedConcurrentlyException(ex);
        }
    }

    private const string AllocationIndex = "ux_fund_allocations_fund_wallet";
}
