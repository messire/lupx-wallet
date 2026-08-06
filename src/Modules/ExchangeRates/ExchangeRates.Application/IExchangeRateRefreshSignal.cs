namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Сигнал фоновому ExchangeRateRefreshBackgroundService (ADR-0001 п.3, ADR-0011) —
/// реализация в Infrastructure использует Channel, чтобы не блокировать вызывающий поток
/// и не делать HTTP-запрос синхронно внутри чужой транзакции (ADR-0011).
/// </summary>
public interface IExchangeRateRefreshSignal
{
    /// <summary>
    /// Ручное обновление (POST /exchange-rates/refresh) уже выполнено вызывающим кодом
    /// (RefreshExchangeRatesCommand отправлен и обработан синхронно, ответ уже сформирован) —
    /// фоновому сервису нужно только перезапустить суточный отсчёт, не выполнять обновление
    /// повторно.
    /// </summary>
    void NotifyManualRefreshCompleted();

    /// <summary>
    /// Запрашивает у фонового сервиса выполнить обновление курсов асинхронно, вне текущей
    /// транзакции (например, реакция на PrimaryWalletChanged, ADR-0001 п.5 / ADR-0011) —
    /// в отличие от NotifyManualRefreshCompleted, сам сигнал не означает, что обновление уже
    /// произошло, фоновый сервис обязан его выполнить.
    /// </summary>
    void RequestRefresh();
}
