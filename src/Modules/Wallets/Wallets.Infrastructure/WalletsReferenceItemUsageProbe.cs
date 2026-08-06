using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Wallets.Infrastructure;

/// <summary>
/// ADR-0009, случай c — одна из реализаций ReferenceData.Application.IReferenceItemUsageProbe:
/// владелец данных Wallets. Проверяет WalletType/Currency по факту использования в
/// wallets.wallets (точечный EXISTS, не выгрузка коллекций). OperationType этому модулю не
/// релевантен — возвращает false. Зарегистрирована в AddWalletsModule.
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
