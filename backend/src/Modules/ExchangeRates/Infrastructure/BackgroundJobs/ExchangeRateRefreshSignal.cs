using System.Threading.Channels;
using System.Transactions;
using LupexWallet.ExchangeRates.Application;

namespace LupexWallet.ExchangeRates.Infrastructure;

public enum ExchangeRateRefreshSignalKind
{
    ManualRefreshCompleted,
    RefreshRequested,
}

/// <summary>
/// IExchangeRateRefreshSignal implementation via Channel (ADR-0004 p.3/p.5, ADR-0001 §7) —
/// Singleton, so the same channel is shared by both the calling code (endpoint/event
/// subscribers) and the background service (ExchangeRateRefreshBackgroundService).
/// </summary>
public sealed class ExchangeRateRefreshSignal : IExchangeRateRefreshSignal
{
    private readonly Channel<ExchangeRateRefreshSignalKind> _channel = Channel.CreateUnbounded<ExchangeRateRefreshSignalKind>();

    public ChannelReader<ExchangeRateRefreshSignalKind> Reader => _channel.Reader;

    public void NotifyManualRefreshCompleted() => _channel.Writer.TryWrite(ExchangeRateRefreshSignalKind.ManualRefreshCompleted);

    /// <summary>
    /// Event subscribers run inside the publisher's TransactionScope (ADR-0001 §5). Writing to the
    /// channel at once would let the background pass read data that is not committed yet — it would
    /// refresh against the old primary currency — or act on a transaction that is then rolled back.
    /// So inside a transaction the write waits for TransactionCompleted and happens only on commit.
    /// </summary>
    public void RequestRefresh()
    {
        var transaction = Transaction.Current;
        if (transaction is null)
        {
            WriteRefreshRequested();
            return;
        }

        transaction.TransactionCompleted += (_, args) =>
        {
            if (args.Transaction?.TransactionInformation.Status == TransactionStatus.Committed)
            {
                WriteRefreshRequested();
            }
        };
    }

    private void WriteRefreshRequested() => _channel.Writer.TryWrite(ExchangeRateRefreshSignalKind.RefreshRequested);
}
