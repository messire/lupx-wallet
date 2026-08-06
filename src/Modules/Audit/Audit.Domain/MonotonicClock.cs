namespace LupexWallet.Audit.Domain;

/// <summary>
/// Гарантированно строго возрастающие в пределах процесса метки времени (ADR-0010) —
/// нужны, чтобы GET /audit-entries мог использовать occurred_at как единственное поле
/// курсора без дублей/пропусков между страницами. Без этого несколько AuditEntry, созданных
/// быстрее разрешения системных часов (например, каскадный пересчёт истории баланса за одну
/// команду порождает много BalanceSnapshotUpdated подряд, docs/PROGRESS.md, риск №3 плана),
/// получили бы одинаковый occurred_at, и курсорная пагинация не могла бы однозначно
/// продолжить страницу. На практике сдвиг от реального времени — доли микросекунды.
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
