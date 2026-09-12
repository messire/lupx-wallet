using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// ADR-0009, case a — one of the two implementations of
/// Wallets.Application.IWalletHistorySource: data owner is Operations. True if the wallet has
/// at least one Operation (including both transfer legs, stored as plain Operation rows, see
/// Transfer.Create). Registered in AddOperationsModule.
/// </summary>
public sealed class OperationsWalletHistorySource(OperationsDbContext dbContext) : IWalletHistorySource
{
    public Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.Operations.AnyAsync(x => x.WalletId == walletId, cancellationToken);
}
