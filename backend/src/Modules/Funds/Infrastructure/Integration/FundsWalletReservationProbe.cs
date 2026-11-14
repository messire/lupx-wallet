using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>
/// The only implementation of Wallets.Application.IWalletReservationProbe (ADR-0010 §4): true if an
/// active fund holds a non-zero allocation on the wallet. Released (zero) allocations do not count.
/// </summary>
public sealed class FundsWalletReservationProbe(FundsDbContext dbContext) : IWalletReservationProbe
{
    public Task<bool> HasActiveReservationsAsync(WalletId walletId, CancellationToken cancellationToken) =>
        (from allocation in dbContext.FundAllocations
         join fund in dbContext.Funds on allocation.FundId equals fund.Id
         where fund.Status == FundStatus.Active && allocation.WalletId == walletId && allocation.Amount != 0m
         select allocation.Id).AnyAsync(cancellationToken);
}
