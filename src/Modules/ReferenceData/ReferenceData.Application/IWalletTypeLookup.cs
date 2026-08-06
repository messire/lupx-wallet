using LupexWallet.SharedKernel;

namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем ReferenceData для модуля Wallets
/// (W2.5, случай d — направление "сверху вниз", как ICurrencyLookup/IOperationTypeLookup,
/// ADR-0007) — разрешение существования и активности типа кошелька, нужное для валидации
/// walletTypeId при создании/изменении Wallet (CreateWalletCommand/UpdateWalletCommand;
/// ранее не проверялось вовсе, см. docs/PROGRESS.md, "Известные упрощения").
/// </summary>
public interface IWalletTypeLookup
{
    Task<WalletTypeLookupResult?> GetAsync(WalletTypeId id, CancellationToken cancellationToken);
}

public sealed record WalletTypeLookupResult(WalletTypeId Id, string Name, bool IsActive);
