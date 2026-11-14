using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// ADR-0002, §2 — the only implementation of Wallets.Application.IWalletHistorySource: data
/// owner is Operations. True if the wallet has at least one Operation row — soft-deleted ones
/// count (restoration must stay possible), as do both transfer legs (plain Operation rows, see
/// Transfer.Create). Deliberately no deleted_at filter. Balance corrections do not count: they are system rows,
/// not facts entered by the user (ADR-0002, §1; ADR-0003, §6). Registered in AddOperationsModule.
/// </summary>
public sealed class OperationsWalletHistorySource(OperationsDbContext dbContext) : IWalletHistorySource
{
    public Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.Operations.AnyAsync(x => x.WalletId == walletId && !x.IsCorrection, cancellationToken);
}
