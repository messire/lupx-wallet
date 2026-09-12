using System.Threading.Channels;
using LupexWallet.ExchangeRates.Application;

namespace LupexWallet.ExchangeRates.Infrastructure;

internal enum ExchangeRateRefreshSignalKind
{
    ManualRefreshCompleted,
    RefreshRequested,
}

/// <summary>
/// IExchangeRateRefreshSignal implementation via Channel (ADR-0001 p.3/p.5, ADR-0011) —
/// Singleton, so the same channel is shared by both the calling code (endpoint/
/// PrimaryWalletChanged handler) and the background service (ExchangeRateRefreshBackgroundService).
/// </summary>
public sealed class ExchangeRateRefreshSignal : IExchangeRateRefreshSignal
{
    private readonly Channel<ExchangeRateRefreshSignalKind> _channel = Channel.CreateUnbounded<ExchangeRateRefreshSignalKind>();

    internal ChannelReader<ExchangeRateRefreshSignalKind> Reader => _channel.Reader;

    public void NotifyManualRefreshCompleted() => _channel.Writer.TryWrite(ExchangeRateRefreshSignalKind.ManualRefreshCompleted);

    public void RequestRefresh() => _channel.Writer.TryWrite(ExchangeRateRefreshSignalKind.RefreshRequested);
}
