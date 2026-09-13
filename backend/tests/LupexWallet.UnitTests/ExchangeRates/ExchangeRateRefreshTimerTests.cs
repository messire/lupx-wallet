using FluentAssertions;
using LupexWallet.ExchangeRates.Application;
using Xunit;

namespace LupexWallet.UnitTests.ExchangeRates;

/// <summary>
/// Чистая логика суточного планирования (ADR-0001 п.2/3) на фейковых часах — без реального
/// Task.Delay/BackgroundService. Проверяет docs/PROGRESS.md, W1.1 DoD п.4: "ручное
/// обновление сбрасывает суточный таймер".
/// </summary>
public sealed class ExchangeRateRefreshTimerTests
{
    [Fact]
    public void GetDelayUntilNextRun_ImmediatelyAfterLastRun_ReturnsFullInterval()
    {
        var lastRunAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var timer = new ExchangeRateRefreshTimer(lastRunAt);

        var delay = timer.GetDelayUntilNextRun(lastRunAt);

        delay.Should().Be(ExchangeRateRefreshTimer.RefreshInterval);
    }

    [Fact]
    public void GetDelayUntilNextRun_AfterIntervalElapsed_ReturnsZero()
    {
        var lastRunAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var timer = new ExchangeRateRefreshTimer(lastRunAt);

        var delay = timer.GetDelayUntilNextRun(lastRunAt + ExchangeRateRefreshTimer.RefreshInterval + TimeSpan.FromMinutes(1));

        delay.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void GetDelayUntilNextRun_PartwayThroughInterval_ReturnsRemainder()
    {
        var lastRunAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var timer = new ExchangeRateRefreshTimer(lastRunAt);
        var now = lastRunAt.AddHours(10);

        var delay = timer.GetDelayUntilNextRun(now);

        delay.Should().Be(TimeSpan.FromHours(14));
    }

    [Fact]
    public void Reset_ManualRefresh_RestartsCountdownFromNewMoment()
    {
        var lastRunAt = new DateTimeOffset(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);
        var timer = new ExchangeRateRefreshTimer(lastRunAt);
        var manualRefreshAt = lastRunAt.AddHours(5); // задолго до истечения суточного интервала

        timer.Reset(manualRefreshAt);

        timer.LastRunAt.Should().Be(manualRefreshAt);
        timer.GetDelayUntilNextRun(manualRefreshAt).Should().Be(ExchangeRateRefreshTimer.RefreshInterval);
        // Автообновление, которое случилось бы через 24ч от исходного lastRunAt, больше не
        // актуально — до следующего планового прогона снова полные 24ч от момента ручного обновления.
        timer.GetDelayUntilNextRun(lastRunAt.AddHours(24)).Should().Be(TimeSpan.FromHours(5));
    }
}
