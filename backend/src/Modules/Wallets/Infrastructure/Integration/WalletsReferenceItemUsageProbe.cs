using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>
/// ADR-0009, case c — a Wallets-owned implementation of
/// ReferenceData.Application.IReferenceItemUsageProbe. Checks WalletType/Currency usage in
/// wallets.wallets via a targeted EXISTS query. OperationType is not relevant here and
/// returns false. Registered in AddWalletsModule.
/// </summary>
public sealed class WalletsReferenceItemUsageProbe(WalletsDbContext dbContext) : IReferenceItemUsageProbe
{
    public Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken) =>
        kind switch
        {
            ReferenceItemKind.WalletType => dbContext.Wallets.AnyAsync(
                w => w.WalletTypeId == new WalletTypeId(referenceItemId), cancellationToken),
            ReferenceItemKind.Currency => dbContext.Wallets.AnyAsync(
                w => w.CurrencyId == new CurrencyId(referenceItemId), cancellationToken),
            ReferenceItemKind.OperationType => Task.FromResult(false),
            _ => Task.FromResult(false),
        };
}
