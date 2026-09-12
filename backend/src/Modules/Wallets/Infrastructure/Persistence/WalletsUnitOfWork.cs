using LupexWallet.Wallets.Application;

namespace LupexWallet.Wallets.Infrastructure;

public sealed class WalletsUnitOfWork(WalletsDbContext dbContext) : IWalletsUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
