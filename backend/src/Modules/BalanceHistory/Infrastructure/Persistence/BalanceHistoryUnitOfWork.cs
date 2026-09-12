using LupexWallet.BalanceHistory.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.BalanceHistory.Infrastructure;

public sealed class BalanceHistoryUnitOfWork(BalanceHistoryDbContext dbContext) : IBalanceHistoryUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({walletId.Value.ToString()}, 0))", cancellationToken);
}
