namespace LupexWallet.Planning.Domain;

/// <summary>
/// Repetition rule of a planned transaction (ADR-0010 §6): frequency, interval N and an optional end —
/// until a date (inclusive) or after K occurrences. Day of week/month/year comes from the first
/// occurrence date; a missing day of month is clamped to the month's last day (see OccurrenceSchedule).
/// </summary>
public sealed record RecurrenceRule
{
    public const int MaxInterval = 1000;

    public RecurrenceFrequency Frequency { get; }
    public int Interval { get; }

    /// <summary>Last date (inclusive) an occurrence may fall on; null — no end date.</summary>
    public DateOnly? Until { get; }

    /// <summary>Total number of occurrences including the first one; null — no limit.</summary>
    public int? Count { get; }

    private RecurrenceRule(RecurrenceFrequency frequency, int interval, DateOnly? until, int? count)
    {
        Frequency = frequency;
        Interval = interval;
        Until = until;
        Count = count;
    }

    public static RecurrenceRule Create(RecurrenceFrequency frequency, int interval, DateOnly? until, int? count)
    {
        if (!Enum.IsDefined(frequency))
        {
            throw new RecurrenceRuleInvalidException("Неизвестная частота повторения.");
        }

        if (interval < 1 || interval > MaxInterval)
        {
            throw new RecurrenceRuleInvalidException($"Интервал повторения должен быть от 1 до {MaxInterval}.");
        }

        if (until is not null && count is not null)
        {
            throw new RecurrenceRuleInvalidException("Укажите окончание повторения либо датой, либо числом повторов, но не обоими.");
        }

        if (count is < 1)
        {
            throw new RecurrenceRuleInvalidException("Число повторов должно быть не меньше 1.");
        }

        return new RecurrenceRule(frequency, interval, until, count);
    }
}
