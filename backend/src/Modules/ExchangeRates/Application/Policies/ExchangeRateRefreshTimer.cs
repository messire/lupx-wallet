namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Pure scheduling logic for the daily auto-refresh (ADR-0001 p.2/3) — extracted from
/// ExchangeRateRefreshBackgroundService (Infrastructure) to be testable without a real
/// Task.Delay/BackgroundService (like
/// BalanceHistory.Infrastructure.BalanceSnapshotSchedulerHostedService.NextRunDelay, also a
/// pure static method). Does not read DateTime.UtcNow itself — the caller passes "now"
/// explicitly, which makes the class testable against a fake clock.
/// </summary>
public sealed class ExchangeRateRefreshTimer(DateTimeOffset initialLastRunAt)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);

    public DateTimeOffset LastRunAt { get; private set; } = initialLastRunAt;

    /// <summary>Delay until the next scheduled run, measured from LastRunAt (never negative).</summary>
    public TimeSpan GetDelayUntilNextRun(DateTimeOffset now)
    {
        var delay = LastRunAt + RefreshInterval - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    /// <summary>
    /// A manual refresh (or a successful/failed scheduled run) resets the daily countdown
    /// from this moment (ADR-0001 p.3).
    /// </summary>
    public void Reset(DateTimeOffset at) => LastRunAt = at;
}
