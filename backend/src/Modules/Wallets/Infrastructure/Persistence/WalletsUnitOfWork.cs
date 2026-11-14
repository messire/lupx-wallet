using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Wallets.Infrastructure;

public sealed class WalletsUnitOfWork(WalletsDbContext dbContext) : IWalletsUnitOfWork
{
    public Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken) =>
        WalletAdvisoryLock.AcquireAsync(dbContext.Database, walletId, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
