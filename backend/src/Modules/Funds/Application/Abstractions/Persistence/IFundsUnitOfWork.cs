using LupexWallet.SharedKernel;

namespace LupexWallet.Funds.Application;

/// <summary>Thin abstraction over FundsDbContext.SaveChangesAsync.</summary>
public interface IFundsUnitOfWork
{
    /// <summary>
    /// Takes the per-wallet advisory lock shared with Wallets and Operations. Every command that
    /// changes reserves on a wallet takes it first, so two reserves cannot both pass the free-funds check.
    /// </summary>
    Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken);

    /// <summary>Saves changes; a concurrent change of the same fund becomes FundModifiedConcurrentlyException.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}
