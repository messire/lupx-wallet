using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем Wallets для Reporting (Total Amount,
/// UC-19/UC-21). Направление "сверху вниз" — Reporting.Application ссылается на
/// Wallets.Application напрямую (Reporting — самый верхний модуль в графе зависимостей,
/// циклов не возникает), как уже делает ExchangeRates.Application.IExchangeRateLookup для
/// того же потребителя (W1.1). Поэтому это не случай ADR-0009 (порт объявлен у владельца
/// данных, не у потребителя).
/// </summary>
public interface IWalletTotalsSource
{
    /// <summary>
    /// Все кошельки, включая архивные (Q16: IncludeInTotal не зависит от IsArchived —
    /// решение о включении в сумму принимает потребитель по IncludeInTotal, не по
    /// архивности).
    /// </summary>
    Task<IReadOnlyList<WalletTotalInfo>> GetAllAsync(CancellationToken cancellationToken);
}

public sealed record WalletTotalInfo(
    WalletId WalletId,
    CurrencyId CurrencyId,
    decimal CurrentBalance,
    bool IncludeInTotal,
    bool IsPrimary);
