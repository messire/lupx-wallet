using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>
/// Implementation of IWalletTotalsSource (Reporting, W2.1) — CurrentBalance/Money is a
/// computed aggregate property that EF Core cannot translate into a SQL projection, so
/// wallets are materialized in full (the set is small; unlike ListWalletsQuery/GET
/// /wallets, no pagination is needed).
/// </summary>
public sealed class WalletTotalsSource(WalletsDbContext dbContext) : IWalletTotalsSource
{
    public async Task<IReadOnlyList<WalletTotalInfo>> GetAllAsync(CancellationToken cancellationToken)
    {
        var wallets = await dbContext.Wallets.AsNoTracking().ToListAsync(cancellationToken);

        return wallets
            .Select(w => new WalletTotalInfo(w.Id, w.CurrencyId, w.CurrentBalance.Amount, w.IncludeInTotal, w.IsPrimary))
            .ToList();
    }
}
