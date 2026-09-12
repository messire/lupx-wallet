using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;

namespace LupexWallet.Wallets.Infrastructure;

public sealed class WalletBalanceGateway(IWalletRepository repository, IWalletsUnitOfWork unitOfWork) : IWalletBalanceGateway
{
    public async Task<WalletBalanceInfo?> GetAsync(WalletId id, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(id, cancellationToken);
        return wallet is null
            ? null
            : new WalletBalanceInfo(
                wallet.Id, wallet.CurrencyId, wallet.InitialBalance, wallet.CurrentBalance,
                wallet.AccountingStartDate, wallet.IsArchived);
    }

    public async Task ApplyDeltaAsync(WalletId id, Money delta, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(id, cancellationToken)
            ?? throw new WalletNotFoundException(id.Value);

        wallet.ApplyBalanceDelta(delta);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
