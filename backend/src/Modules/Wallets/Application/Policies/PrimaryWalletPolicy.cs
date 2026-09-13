using LupexWallet.Wallets.Domain;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Enforces the "exactly one primary wallet" invariant across the whole aggregate
/// collection (ddd-model.md, §2.1) — cannot be enforced by a single Wallet, so it lives as
/// an Application-level domain service working through IWalletRepository.
/// </summary>
public sealed class PrimaryWalletPolicy(IWalletRepository repository, IWalletsUnitOfWork unitOfWork)
{
    /// <summary>Whether at least one wallet already exists — determines whether a new one becomes primary.</summary>
    public async Task<bool> IsFirstWalletAsync(CancellationToken cancellationToken) =>
        !await repository.AnyExistsAsync(cancellationToken);

    /// <summary>
    /// UC-05: transfers the primary flag to <paramref name="wallet"/> — unmarks the previous
    /// primary (if different) via Wallet.UnmarkAsPrimary, then marks the target via
    /// Wallet.MarkAsPrimary. Idempotent: no-op if the wallet is already primary.
    ///
    /// Unmarking the previous primary is saved via a separate SaveChangesAsync BEFORE
    /// marking the new one, because of ux_wallets_single_primary (a non-deferrable partial
    /// unique index, schema.md): without the split, both rows could momentarily have
    /// is_primary=true and violate the index (EF Core does not guarantee UPDATE ordering
    /// between unrelated entities of the same type — observed in practice). Both calls stay
    /// within the same command transaction (TransactionBehavior), so rollback remains atomic.
    /// </summary>
    public async Task SetPrimaryAsync(Wallet wallet, CancellationToken cancellationToken)
    {
        if (wallet.IsPrimary)
        {
            return;
        }

        var currentPrimary = await repository.GetCurrentPrimaryAsync(cancellationToken);
        if (currentPrimary is not null && currentPrimary.Id != wallet.Id)
        {
            currentPrimary.UnmarkAsPrimary();
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        wallet.MarkAsPrimary(currentPrimary?.Id);
    }
}
