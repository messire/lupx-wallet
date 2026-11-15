namespace LupexWallet.Planning.Domain;

/// <summary>
/// Pure expansion of a schedule into original occurrence dates (ADR-0010 §6: occurrences are computed,
/// never stored). The n-th occurrence is always derived from the first date, not from the previous one,
/// so a clamped month end does not drift: 31 Jan monthly → 29 Feb (leap year) → 31 Mar → 30 Apr.
/// Yearly on 29 Feb falls on 28 Feb in common years.
/// </summary>
public static class OccurrenceSchedule
{
    /// <summary>Original dates with <paramref name="from"/> ≤ date ≤ <paramref name="to"/>, ascending.</summary>
    public static IEnumerable<DateOnly> Expand(DateOnly startDate, RecurrenceRule? rule, DateOnly from, DateOnly to)
    {
        if (from > to)
        {
            yield break;
        }

        if (rule is null)
        {
            if (startDate >= from && startDate <= to)
            {
                yield return startDate;
            }

            yield break;
        }

        for (var index = FirstCandidateIndex(startDate, rule, from); ; index++)
        {
            if (rule.Count is { } count && index >= count)
            {
                yield break;
            }

            if (TryGetOccurrence(startDate, rule, index) is not { } date)
            {
                yield break;
            }

            if (date > to || (rule.Until is { } until && date > until))
            {
                yield break;
            }

            if (date >= from)
            {
                yield return date;
            }
        }
    }

    /// <summary>Whether <paramref name="date"/> is an original occurrence date of the schedule.</summary>
    public static bool IsOccurrence(DateOnly startDate, RecurrenceRule? rule, DateOnly date) =>
        Expand(startDate, rule, date, date).Any();

    /// <summary>The <paramref name="index"/>-th occurrence (0 = the first date), ignoring the rule's end; null past DateOnly.MaxValue.</summary>
    public static DateOnly? TryGetOccurrence(DateOnly startDate, RecurrenceRule rule, long index)
    {
        try
        {
            var step = checked(index * rule.Interval);
            return rule.Frequency switch
            {
                RecurrenceFrequency.Daily => startDate.AddDays(checked((int)step)),
                RecurrenceFrequency.Weekly => startDate.AddDays(checked((int)(step * 7))),
                // DateOnly.AddMonths/AddYears clamp a missing day to the last day of the month.
                RecurrenceFrequency.Monthly => startDate.AddMonths(checked((int)step)),
                RecurrenceFrequency.Yearly => startDate.AddYears(checked((int)step)),
                _ => throw new ArgumentOutOfRangeException(nameof(rule)),
            };
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
        {
            return null;
        }
    }

    /// <summary>An index whose occurrence is not after <paramref name="from"/> (or 0), so iteration starts close to the range.</summary>
    private static long FirstCandidateIndex(DateOnly startDate, RecurrenceRule rule, DateOnly from)
    {
        if (from <= startDate)
        {
            return 0;
        }

        long candidate = rule.Frequency switch
        {
            RecurrenceFrequency.Daily => (from.DayNumber - startDate.DayNumber) / rule.Interval,
            RecurrenceFrequency.Weekly => (from.DayNumber - startDate.DayNumber) / (7L * rule.Interval),
            RecurrenceFrequency.Monthly => MonthsBetween(startDate, from) / rule.Interval - 1,
            RecurrenceFrequency.Yearly => (from.Year - startDate.Year) / rule.Interval - 1,
            _ => 0,
        };

        return Math.Max(0, candidate);
    }

    private static long MonthsBetween(DateOnly a, DateOnly b) => (b.Year - a.Year) * 12L + (b.Month - a.Month);
}
