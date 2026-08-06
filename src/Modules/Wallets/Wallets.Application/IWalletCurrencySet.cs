using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Узкий read-only контракт, публикуемый модулем Wallets для модуля ExchangeRates (W1.1,
/// направление "сверху вниз" — ExchangeRates уже ссылается на Wallets.Application, как
/// BalanceHistory делает это для IWalletDirectory/IWalletBalanceGateway, ADR-0008) — набор
/// валют, реально используемых кошельками, и текущая основная валюта (ADR-0001 п.1/4:
/// курсы запрашиваются только для пар "валюта кошелька → основная валюта", реально
/// встречающихся, а не для полной матрицы валют справочника). Архивные кошельки
/// учитываются — их история/баланс по-прежнему может отображаться в основной валюте.
/// </summary>
public interface IWalletCurrencySet
{
    Task<WalletCurrencySetResult> GetAsync(CancellationToken cancellationToken);
}

public sealed record WalletCurrencySetResult(CurrencyId? PrimaryCurrencyId, IReadOnlyList<CurrencyId> AllWalletCurrencyIds);
