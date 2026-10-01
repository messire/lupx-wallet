using LupexWallet.BalanceHistory.Application;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Infrastructure;

public sealed class BalanceHistoryUnitOfWork(BalanceHistoryDbContext dbContext) : IBalanceHistoryUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken) =>
        WalletAdvisoryLock.AcquireAsync(dbContext.Database, walletId, cancellationToken);
}
