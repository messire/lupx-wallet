using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

public sealed class WalletDirectory(WalletsDbContext dbContext) : IWalletDirectory
{
    public async Task<IReadOnlyList<WalletId>> GetAllWalletIdsAsync(CancellationToken cancellationToken) =>
        await dbContext.Wallets.Select(w => w.Id).ToListAsync(cancellationToken);
}
