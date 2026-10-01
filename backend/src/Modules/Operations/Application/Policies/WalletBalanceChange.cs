using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Operations.Application;

internal static class WalletBalanceChange
{
    /// <summary>
    /// Applies the command's single net delta to the wallet. A zero delta (for example the
    /// change is absorbed by a later stated balance, ADR-0003, §6) leaves the wallet untouched.
    /// </summary>
    public static Task ApplyAsync(
        IWalletBalanceGateway walletBalanceGateway, WalletId walletId, Money delta, CancellationToken cancellationToken) =>
        delta.Amount == 0m ? Task.CompletedTask : walletBalanceGateway.ApplyDeltaAsync(walletId, delta, cancellationToken);
}
