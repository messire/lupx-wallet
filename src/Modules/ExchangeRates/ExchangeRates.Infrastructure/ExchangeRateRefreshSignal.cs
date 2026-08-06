using System.Threading.Channels;
using LupexWallet.ExchangeRates.Application;

namespace LupexWallet.ExchangeRates.Infrastructure;

internal enum ExchangeRateRefreshSignalKind
{
    ManualRefreshCompleted,
    RefreshRequested,
}

/// <summary>
/// Реализация IExchangeRateRefreshSignal через Channel (ADR-0001 п.3/п.5, ADR-0011) —
/// Singleton, чтобы один и тот же канал использовался и вызывающим кодом (эндпоинт/
/// обработчик PrimaryWalletChanged), и фоновым сервисом (ExchangeRateRefreshBackgroundService).
/// </summary>
public sealed class ExchangeRateRefreshSignal : IExchangeRateRefreshSignal
{
    private readonly Channel<ExchangeRateRefreshSignalKind> _channel = Channel.CreateUnbounded<ExchangeRateRefreshSignalKind>();

    internal ChannelReader<ExchangeRateRefreshSignalKind> Reader => _channel.Reader;

    public void NotifyManualRefreshCompleted() => _channel.Writer.TryWrite(ExchangeRateRefreshSignalKind.ManualRefreshCompleted);

    public void RequestRefresh() => _channel.Writer.TryWrite(ExchangeRateRefreshSignalKind.RefreshRequested);
}
