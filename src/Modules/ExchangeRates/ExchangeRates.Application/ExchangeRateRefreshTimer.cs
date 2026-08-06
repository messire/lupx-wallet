namespace LupexWallet.ExchangeRates.Application;

/// <summary>
/// Чистая логика планирования суточного автообновления (ADR-0001 п.2/3) — вынесена из
/// ExchangeRateRefreshBackgroundService (Infrastructure), чтобы быть тестируемой без
/// реального Task.Delay/BackgroundService (по аналогии с
/// BalanceHistory.Infrastructure.BalanceSnapshotSchedulerHostedService.NextRunDelay, который
/// тоже вынесен в чистый статический метод). Не хранит DateTime.UtcNow сама — вызывающий
/// код передаёт "текущее время" явно, что и делает класс тестируемым на фейковых часах.
/// </summary>
public sealed class ExchangeRateRefreshTimer(DateTimeOffset initialLastRunAt)
{
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromDays(1);

    public DateTimeOffset LastRunAt { get; private set; } = initialLastRunAt;

    /// <summary>Задержка до следующего планового прогона, отсчитываемая от LastRunAt (никогда не отрицательная).</summary>
    public TimeSpan GetDelayUntilNextRun(DateTimeOffset now)
    {
        var delay = LastRunAt + RefreshInterval - now;
        return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
    }

    /// <summary>
    /// Ручное обновление (или успешный/неуспешный плановый прогон) сбрасывает суточный
    /// отсчёт заново от этого момента (ADR-0001 п.3).
    /// </summary>
    public void Reset(DateTimeOffset at) => LastRunAt = at;
}
