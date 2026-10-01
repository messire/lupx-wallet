using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

public sealed class WalletCurrencySet(WalletsDbContext dbContext) : IWalletCurrencySet
{
    public async Task<WalletCurrencySetResult> GetAsync(CancellationToken cancellationToken)
    {
        var primaryCurrencyId = await dbContext.Wallets
            .Where(w => w.IsPrimary)
            .Select(w => (CurrencyId?)w.CurrencyId)
            .FirstOrDefaultAsync(cancellationToken);

        var allCurrencyIds = await dbContext.Wallets
            .Select(w => w.CurrencyId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var earliestStartDate = await dbContext.Wallets
            .Select(w => (DateOnly?)w.AccountingStartDate)
            .MinAsync(cancellationToken);

        return new WalletCurrencySetResult(primaryCurrencyId, allCurrencyIds, earliestStartDate);
    }
}
