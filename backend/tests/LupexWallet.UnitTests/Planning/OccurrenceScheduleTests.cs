using FluentAssertions;
using LupexWallet.Planning.Domain;
using Xunit;

namespace LupexWallet.UnitTests.Planning;

/// <summary>Чистое развёртывание расписания (ADR-0010 §6): интервалы, конец месяца, високосный год, окончание.</summary>
public sealed class OccurrenceScheduleTests
{
    private static DateOnly D(int y, int m, int d) => new(y, m, d);

    private static RecurrenceRule Rule(RecurrenceFrequency frequency, int interval = 1, DateOnly? until = null, int? count = null) =>
        RecurrenceRule.Create(frequency, interval, until, count);

    private static List<DateOnly> Expand(DateOnly start, RecurrenceRule? rule, DateOnly from, DateOnly to) =>
        OccurrenceSchedule.Expand(start, rule, from, to).ToList();

    [Fact]
    public void OneOff_YieldsStartDateOnlyInsideRange()
    {
        Expand(D(2026, 3, 10), null, D(2026, 3, 1), D(2026, 3, 31)).Should().Equal(D(2026, 3, 10));
        Expand(D(2026, 3, 10), null, D(2026, 3, 11), D(2026, 3, 31)).Should().BeEmpty();
        Expand(D(2026, 3, 10), null, D(2026, 3, 10), D(2026, 3, 10)).Should().Equal(D(2026, 3, 10));
    }

    [Fact]
    public void Daily_WithInterval_StepsFromStart()
    {
        Expand(D(2026, 1, 1), Rule(RecurrenceFrequency.Daily, 3), D(2026, 1, 1), D(2026, 1, 12))
            .Should().Equal(D(2026, 1, 1), D(2026, 1, 4), D(2026, 1, 7), D(2026, 1, 10));
    }

    [Fact]
    public void Daily_RangeFarAfterStart_StartsAtFirstOccurrenceInRange()
    {
        Expand(D(2000, 1, 1), Rule(RecurrenceFrequency.Daily, 7), D(2026, 1, 1), D(2026, 1, 20))
            .Should().OnlyContain(d => (d.DayNumber - D(2000, 1, 1).DayNumber) % 7 == 0)
            .And.HaveCount(3);
    }

    [Fact]
    public void Weekly_KeepsWeekdayOfStart()
    {
        // 2026-01-05 is a Monday.
        var dates = Expand(D(2026, 1, 5), Rule(RecurrenceFrequency.Weekly, 2), D(2026, 1, 1), D(2026, 2, 28));
        dates.Should().Equal(D(2026, 1, 5), D(2026, 1, 19), D(2026, 2, 2), D(2026, 2, 16));
        dates.Should().OnlyContain(d => d.DayOfWeek == DayOfWeek.Monday);
    }

    [Fact]
    public void Monthly_On31st_ClampsToMonthEndWithoutDrift()
    {
        Expand(D(2027, 1, 31), Rule(RecurrenceFrequency.Monthly), D(2027, 1, 1), D(2027, 6, 30))
            .Should().Equal(D(2027, 1, 31), D(2027, 2, 28), D(2027, 3, 31), D(2027, 4, 30), D(2027, 5, 31), D(2027, 6, 30));
    }

    [Fact]
    public void Monthly_On31st_LeapYearFebruaryIs29th()
    {
        Expand(D(2028, 1, 31), Rule(RecurrenceFrequency.Monthly), D(2028, 2, 1), D(2028, 3, 31))
            .Should().Equal(D(2028, 2, 29), D(2028, 3, 31));
    }

    [Fact]
    public void Monthly_On30th_FebruaryClampedThenBackTo30th()
    {
        Expand(D(2026, 1, 30), Rule(RecurrenceFrequency.Monthly), D(2026, 1, 1), D(2026, 4, 30))
            .Should().Equal(D(2026, 1, 30), D(2026, 2, 28), D(2026, 3, 30), D(2026, 4, 30));
    }

    [Fact]
    public void Monthly_WithInterval_RangeInTheMiddle()
    {
        // Every 3 months from 2025-11-15: 2026-02-15, 2026-05-15, 2026-08-15 ...
        Expand(D(2025, 11, 15), Rule(RecurrenceFrequency.Monthly, 3), D(2026, 3, 1), D(2026, 9, 1))
            .Should().Equal(D(2026, 5, 15), D(2026, 8, 15));
    }

    [Fact]
    public void Yearly_OnLeapDay_FallsOn28thInCommonYears()
    {
        Expand(D(2024, 2, 29), Rule(RecurrenceFrequency.Yearly), D(2024, 1, 1), D(2028, 12, 31))
            .Should().Equal(D(2024, 2, 29), D(2025, 2, 28), D(2026, 2, 28), D(2027, 2, 28), D(2028, 2, 29));
    }

    [Fact]
    public void Yearly_WithInterval()
    {
        Expand(D(2020, 6, 1), Rule(RecurrenceFrequency.Yearly, 2), D(2021, 1, 1), D(2026, 12, 31))
            .Should().Equal(D(2022, 6, 1), D(2024, 6, 1), D(2026, 6, 1));
    }

    [Fact]
    public void Until_IsInclusive()
    {
        Expand(D(2026, 1, 10), Rule(RecurrenceFrequency.Monthly, until: D(2026, 3, 10)), D(2026, 1, 1), D(2026, 12, 31))
            .Should().Equal(D(2026, 1, 10), D(2026, 2, 10), D(2026, 3, 10));
    }

    [Fact]
    public void Count_LimitsTotalOccurrencesCountedFromStart()
    {
        var rule = Rule(RecurrenceFrequency.Weekly, count: 3);
        Expand(D(2026, 1, 1), rule, D(2026, 1, 1), D(2026, 12, 31))
            .Should().Equal(D(2026, 1, 1), D(2026, 1, 8), D(2026, 1, 15));

        // A range after the start still counts from the first occurrence.
        Expand(D(2026, 1, 1), rule, D(2026, 1, 10), D(2026, 12, 31)).Should().Equal(D(2026, 1, 15));
    }

    [Fact]
    public void RangeBeforeStart_OrInverted_IsEmpty()
    {
        Expand(D(2026, 5, 1), Rule(RecurrenceFrequency.Daily), D(2026, 1, 1), D(2026, 4, 30)).Should().BeEmpty();
        Expand(D(2026, 5, 1), Rule(RecurrenceFrequency.Daily), D(2026, 6, 1), D(2026, 5, 1)).Should().BeEmpty();
    }

    [Fact]
    public void NearMaxDate_StopsWithoutOverflow()
    {
        Expand(D(9999, 12, 1), Rule(RecurrenceFrequency.Monthly), D(9999, 1, 1), DateOnly.MaxValue)
            .Should().Equal(D(9999, 12, 1));
    }

    [Theory]
    [InlineData(2027, 2, 28, true)]
    [InlineData(2027, 2, 27, false)]
    [InlineData(2027, 3, 31, true)]
    [InlineData(2026, 12, 31, false)]
    public void IsOccurrence_MatchesExpansion(int y, int m, int d, bool expected)
    {
        OccurrenceSchedule.IsOccurrence(D(2027, 1, 31), Rule(RecurrenceFrequency.Monthly), D(y, m, d)).Should().Be(expected);
    }

    [Fact]
    public void RecurrenceRule_RejectsInvalidInput()
    {
        FluentActions.Invoking(() => Rule(RecurrenceFrequency.Daily, 0)).Should().Throw<RecurrenceRuleInvalidException>();
        FluentActions.Invoking(() => Rule(RecurrenceFrequency.Daily, RecurrenceRule.MaxInterval + 1)).Should().Throw<RecurrenceRuleInvalidException>();
        FluentActions.Invoking(() => Rule(RecurrenceFrequency.Daily, until: D(2026, 1, 1), count: 2)).Should().Throw<RecurrenceRuleInvalidException>();
        FluentActions.Invoking(() => Rule(RecurrenceFrequency.Daily, count: 0)).Should().Throw<RecurrenceRuleInvalidException>();
        FluentActions.Invoking(() => Rule((RecurrenceFrequency)42)).Should().Throw<RecurrenceRuleInvalidException>();
    }
}
