using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Read-only contract published by Wallets for ExchangeRates — the set of
/// currencies actually used by wallets, the current primary currency and the earliest
/// accounting start date (the left edge of rate history, ADR-0004 §9). ExchangeRates
/// uses the primary currency as the target; refresh source currencies come from the
/// active ReferenceData currencies (ADR-0004 §4).
/// Archived wallets are included — their balance history can still be shown in the
/// primary currency.
/// </summary>
public interface IWalletCurrencySet
{
    Task<WalletCurrencySetResult> GetAsync(CancellationToken cancellationToken);
}

public sealed record WalletCurrencySetResult(
    CurrencyId? PrimaryCurrencyId,
    IReadOnlyList<CurrencyId> AllWalletCurrencyIds,
    DateOnly? EarliestAccountingStartDate);
