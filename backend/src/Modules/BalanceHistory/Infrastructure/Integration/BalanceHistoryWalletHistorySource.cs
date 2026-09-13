using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.BalanceHistory.Infrastructure;

/// <summary>
/// ADR-0009, case a — one of two implementations of Wallets.Application.IWalletHistorySource:
/// the BalanceHistory data owner. True if the wallet has at least one BalanceSnapshot.
/// Registered in AddBalanceHistoryModule.
/// </summary>
public sealed class BalanceHistoryWalletHistorySource(BalanceHistoryDbContext dbContext) : IWalletHistorySource
{
    public Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.BalanceSnapshots.AnyAsync(x => x.WalletId == walletId, cancellationToken);
}
