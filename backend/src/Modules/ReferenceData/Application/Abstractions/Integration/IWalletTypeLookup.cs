using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Narrow read-only contract published by ReferenceData for Wallets (W2.5, case d —
/// top-down direction, like ICurrencyLookup/IOperationTypeLookup, ADR-0007) — resolves
/// wallet type existence and active status, needed to validate walletTypeId when creating/
/// updating a Wallet (CreateWalletCommand/UpdateWalletCommand; previously not validated at
/// all, see docs/PROGRESS.md, "Known simplifications").
/// </summary>
public interface IWalletTypeLookup
{
    Task<WalletTypeLookupResult?> GetAsync(WalletTypeId id, CancellationToken cancellationToken);
}

public sealed record WalletTypeLookupResult(WalletTypeId Id, string Name, bool IsActive);
