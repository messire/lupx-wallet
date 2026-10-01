namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Signal to the background ExchangeRateRefreshBackgroundService (ADR-0004 p.3, ADR-0001 §7) —
/// the Infrastructure implementation uses a Channel so it does not block the calling thread
/// or make an HTTP call synchronously inside someone else's transaction (ADR-0001 §4).
/// </summary>
public interface IExchangeRateRefreshSignal
{
    /// <summary>
    /// A manual refresh (POST /exchange-rates/refresh) has already been performed by the
    /// caller (RefreshExchangeRatesCommand sent and handled synchronously, response already
    /// built) — the background service only needs to restart the daily countdown, not
    /// refresh again.
    /// </summary>
    void NotifyManualRefreshCompleted();

    /// <summary>
    /// Asks the background service for a full pass (RefreshExchangeRatesCommand, then
    /// BackfillExchangeRateHistoryCommand) outside the current transaction — in reaction to
    /// PrimaryWalletChanged, WalletCurrencyChanged, WalletCreated and CurrencyCreated (ADR-0004
    /// §5, §9). Unlike NotifyManualRefreshCompleted, nothing has been refreshed yet.
    /// <para>
    /// Called inside a transaction (subscribers run in the publisher's TransactionScope), the
    /// signal is delivered only after that transaction commits; on rollback it is dropped, so the
    /// background pass never reads state that is not committed yet (ADR-0001 §7). Without an
    /// ambient transaction it is delivered immediately.
    /// </para>
    /// </summary>
    void RequestRefresh();
}
