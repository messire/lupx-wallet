using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// A narrow read-only contract published by the BalanceHistory module for Reporting
/// (historical total, UC-21). "Top-down" direction (see IWalletTotalsSource in
/// Wallets.Application, the same W2.1 pattern) — not an ADR-0009 case. Wallet balance on a
/// date in the wallet's own currency (Reporting handles conversion to the primary currency).
/// </summary>
public interface IWalletBalanceOnDateLookup
{
    /// <summary>
    /// Wallet balance on a date (in the wallet's currency), using the same algorithm as
    /// GetWalletBalanceQuery (Q13: a date before accounting_start_date → 0; otherwise the
    /// snapshot if materialized, or an on-the-fly calculation from InitialBalance + operation
    /// deltas). Null — wallet not found (not expected for a consumer that already enumerated
    /// wallets via IWalletTotalsSource, but the contract stays honest).
    /// </summary>
    Task<decimal?> GetBalanceOnDateAsync(WalletId walletId, DateOnly date, CancellationToken cancellationToken);
}
