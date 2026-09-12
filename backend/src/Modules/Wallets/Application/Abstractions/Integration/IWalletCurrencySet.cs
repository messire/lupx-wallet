using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Read-only contract published by Wallets for ExchangeRates (W1.1) — the set of
/// currencies actually used by wallets and the current primary currency (ADR-0001 §1/4:
/// rates are fetched only for pairs that actually occur, not the full reference matrix).
/// Archived wallets are included — their balance history can still be shown in the
/// primary currency.
/// </summary>
public interface IWalletCurrencySet
{
    Task<WalletCurrencySetResult> GetAsync(CancellationToken cancellationToken);
}

public sealed record WalletCurrencySetResult(CurrencyId? PrimaryCurrencyId, IReadOnlyList<CurrencyId> AllWalletCurrencyIds);
