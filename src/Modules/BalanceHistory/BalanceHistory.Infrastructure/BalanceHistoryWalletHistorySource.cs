using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// ADR-0009, случай a — одна из двух реализаций Wallets.Application.IWalletHistorySource:
/// владелец данных BalanceHistory. true, если у кошелька есть хотя бы один BalanceSnapshot.
/// Зарегистрирована в AddBalanceHistoryModule.
/// </summary>
public sealed class BalanceHistoryWalletHistorySource(BalanceHistoryDbContext dbContext) : IWalletHistorySource
{
    public Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.BalanceSnapshots.AnyAsync(x => x.WalletId == walletId, cancellationToken);
}
