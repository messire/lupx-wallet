using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>
/// Реализация IWalletTotalsSource (Reporting, W2.1) — CurrentBalance/Money вычисляемое
/// свойство агрегата, EF Core не может транслировать его в SQL-проекцию, поэтому кошельки
/// материализуются целиком (набор кошельков небольшой, постраничности не требуется —
/// в отличие от ListWalletsQuery/GET /wallets).
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
