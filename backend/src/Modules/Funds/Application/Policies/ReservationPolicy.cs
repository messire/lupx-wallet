using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Funds.Application;

/// <summary>
/// ADR-0010 §4, B7: a new or increased allocation must fit into the wallet's free funds =
/// current balance − allocations of all active funds on the wallet. A collection invariant over
/// all funds (like PrimaryWalletPolicy in Wallets). The caller holds the wallet lock
/// (IFundsUnitOfWork.AcquireWalletLockAsync), so two reserves on one wallet cannot both pass.
/// </summary>
public sealed class ReservationPolicy(IFundRepository repository, IWalletBalanceGateway walletBalanceGateway)
{
    public async Task ReserveAsync(Fund fund, WalletId walletId, decimal amount, CancellationToken cancellationToken)
    {
        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new FundWalletNotFoundException();

        fund.EnsureCanReserve(wallet.CurrencyId, amount);

        if (wallet.IsArchived)
        {
            throw new FundWalletArchivedException();
        }

        var reserved = await repository.GetReservedOnWalletAsync(walletId, cancellationToken);
        var free = ReservationCoverage.Free(wallet.CurrentBalance.Amount, reserved);
        if (amount > free)
        {
            throw new FundInsufficientFreeFundsException(Math.Max(free, 0m));
        }

        fund.Reserve(walletId, wallet.CurrencyId, amount);
    }
}
