using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем BalanceHistory для Reporting
/// (историческая сумма, UC-21). Направление "сверху вниз" (см. IWalletTotalsSource в
/// Wallets.Application, тот же прием W2.1) — не случай ADR-0009. Баланс кошелька на дату в
/// его собственной валюте (конвертацией в валюту основного кошелька занимается Reporting).
/// </summary>
public interface IWalletBalanceOnDateLookup
{
    /// <summary>
    /// Баланс кошелька на дату (в валюте кошелька), с тем же алгоритмом, что и
    /// GetWalletBalanceQuery (Q13: дата раньше accounting_start_date → 0; иначе — слепок,
    /// если материализован, либо расчет на лету из InitialBalance + дельты операций).
    /// Null — кошелек не найден (не ожидается для потребителя, который уже перечислил
    /// кошельки через IWalletTotalsSource, но контракт остаётся честным).
    /// </summary>
    Task<decimal?> GetBalanceOnDateAsync(WalletId walletId, DateOnly date, CancellationToken cancellationToken);
}
