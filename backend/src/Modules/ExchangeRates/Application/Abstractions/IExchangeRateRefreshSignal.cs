namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Signal to the background ExchangeRateRefreshBackgroundService (ADR-0001 p.3, ADR-0011) —
/// the Infrastructure implementation uses a Channel so it does not block the calling thread
/// or make an HTTP call synchronously inside someone else's transaction (ADR-0011).
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
    /// Asks the background service to refresh rates asynchronously, outside the current
    /// transaction (e.g. in reaction to PrimaryWalletChanged, ADR-0001 p.5 / ADR-0011) —
    /// unlike NotifyManualRefreshCompleted, the signal itself does not mean the refresh has
    /// already happened; the background service must perform it.
    /// </summary>
    void RequestRefresh();
}
