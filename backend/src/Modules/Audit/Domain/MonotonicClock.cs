namespace LupexWallet.Audit.Domain;

/// <summary>
/// Timestamps guaranteed strictly increasing within the process (ADR-0010) — needed so
/// GET /audit-entries can use occurred_at as the sole cursor field without duplicates or gaps
/// between pages. Without this, several AuditEntry records created faster than system clock
/// resolution (e.g. a cascading balance history recalculation raising many BalanceSnapshotUpdated
/// events in one command, docs/PROGRESS.md, plan risk #3) would get the same occurred_at, and
/// cursor pagination could not unambiguously continue the page. In practice the drift from real
/// time is a fraction of a microsecond.
/// </summary>
internal static class MonotonicClock
{
    private static long _lastTicks = DateTimeOffset.UtcNow.Ticks;

    public static DateTimeOffset UtcNow()
    {
        while (true)
        {
            var last = Interlocked.Read(ref _lastTicks);
            var nowTicks = DateTimeOffset.UtcNow.Ticks;
            var candidate = nowTicks > last ? nowTicks : last + 1;

            if (Interlocked.CompareExchange(ref _lastTicks, candidate, last) == last)
            {
                return new DateTimeOffset(candidate, TimeSpan.Zero);
            }
        }
    }
}
