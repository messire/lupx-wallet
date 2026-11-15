namespace LupexWallet.Reporting.Application;

/// <param name="Date">Forecast date: the effective/due date, or today for an overdue item.</param>
/// <param name="OriginalDate">Effective/due date before an overdue item was moved to today.</param>
/// <param name="Amount">Signed amount in <paramref name="CurrencyId"/>: positive income, negative expense.</param>
/// <param name="ConvertedAmount">Signed amount in the calculation currency.</param>
public sealed record ForecastMovementDto(
    string Kind,
    DateOnly Date,
    DateOnly OriginalDate,
    bool IsOverdue,
    decimal Amount,
    Guid CurrencyId,
    decimal ConvertedAmount,
    Guid? PlannedTransactionId,
    Guid? WalletId,
    Guid? LoanId,
    string? LoanName);

/// <summary>A date of the forecast with its movements and the expected balance at the end of the day.</summary>
public sealed record ForecastDayDto(
    DateOnly Date, decimal NetChange, decimal ExpectedBalance, bool IsNegative, IReadOnlyList<ForecastMovementDto> Movements);

/// <summary>Inclusive range of days with a negative expected balance.</summary>
public sealed record NegativePeriodDto(DateOnly From, DateOnly To);

public sealed record CashFlowProjection(
    IReadOnlyList<ForecastDayDto> Days,
    decimal EndBalance,
    decimal MinBalance,
    DateOnly MinBalanceDate,
    DateOnly? FirstNegativeDate,
    IReadOnlyList<NegativePeriodDto> NegativePeriods);

/// <summary>
/// Pure part of CashFlowForecast (ADR-0010 §7): applies already converted movements day by day to
/// the start balance. Today is always a point (even without movements); other days only when they
/// have movements. The balance is a step function, so a negative period lasts until the next date
/// with a non-negative balance, or until <c>to</c>. The minimum is taken over the day-end balances.
/// </summary>
public static class CashFlowForecastCalculator
{
    public static CashFlowProjection Project(
        DateOnly today, DateOnly to, decimal startBalance, IReadOnlyList<ForecastMovementDto> movements)
    {
        var byDate = movements
            .Where(m => m.Date >= today && m.Date <= to)
            .GroupBy(m => m.Date)
            .ToDictionary(g => g.Key, g => g.ToList());
        if (!byDate.ContainsKey(today))
        {
            byDate[today] = [];
        }

        var days = new List<ForecastDayDto>();
        var balance = startBalance;
        decimal? minBalance = null;
        var minDate = today;
        DateOnly? firstNegative = null;
        var periods = new List<NegativePeriodDto>();
        DateOnly? periodStart = null;

        foreach (var date in byDate.Keys.Order())
        {
            var dayMovements = byDate[date];
            var net = dayMovements.Sum(m => m.ConvertedAmount);
            balance += net;
            var isNegative = balance < 0m;

            days.Add(new ForecastDayDto(date, net, balance, isNegative, dayMovements));

            if (minBalance is null || balance < minBalance)
            {
                minBalance = balance;
                minDate = date;
            }

            if (isNegative)
            {
                firstNegative ??= date;
                periodStart ??= date;
            }
            else if (periodStart is { } start)
            {
                periods.Add(new NegativePeriodDto(start, date.AddDays(-1)));
                periodStart = null;
            }
        }

        if (periodStart is { } openStart)
        {
            periods.Add(new NegativePeriodDto(openStart, to));
        }

        return new CashFlowProjection(days, balance, minBalance!.Value, minDate, firstNegative, periods);
    }
}
