namespace LupexWallet.Debts.Domain;

/// <summary>
/// Pure schedule rules of a loan (ADR-0010 §3): payments cover the earliest unpaid items first,
/// item statuses are derived, equal monthly payments can be generated.
/// </summary>
public static class LoanScheduleCalculator
{
    /// <summary>
    /// Distributes the counted paid total over the items in order. Matching every payment against the
    /// earliest unpaid items gives the same result as distributing their sum, which is why the sum is enough.
    /// </summary>
    public static IReadOnlyList<LoanScheduleItemState> Evaluate(
        IReadOnlyList<LoanScheduleEntry> orderedItems, decimal countedPaid, DateOnly today)
    {
        var pool = Math.Max(0m, countedPaid);
        var result = new List<LoanScheduleItemState>(orderedItems.Count);
        foreach (var item in orderedItems)
        {
            var paid = Math.Min(item.Amount, pool);
            pool -= paid;

            var status = paid >= item.Amount ? LoanScheduleItemStatus.Paid
                : item.DueDate < today ? LoanScheduleItemStatus.Overdue
                : paid > 0m ? LoanScheduleItemStatus.Partial
                : LoanScheduleItemStatus.Expected;

            result.Add(new LoanScheduleItemState(item.DueDate, item.Amount, paid, status));
        }

        return result;
    }

    /// <summary>The first item that is not fully paid (overdue ones included), or null.</summary>
    public static LoanScheduleItemState? NextPayment(IReadOnlyList<LoanScheduleItemState> states) =>
        states.FirstOrDefault(x => x.Status != LoanScheduleItemStatus.Paid);

    /// <summary>
    /// N equal monthly payments from <paramref name="firstPaymentDate"/> (day of month clamped to the
    /// month's last day). Each payment is the total divided by N, rounded down to 2 decimals; the last
    /// one takes the remainder so the schedule adds up to the total exactly.
    /// </summary>
    public static IReadOnlyList<LoanScheduleEntry> GenerateEqualMonthly(decimal total, DateOnly firstPaymentDate, int numberOfPayments)
    {
        if (numberOfPayments < 1 || numberOfPayments > Loan.MaxGeneratedPayments)
        {
            throw new LoanScheduleGenerationCountOutOfRangeException(Loan.MaxGeneratedPayments);
        }

        var regular = Math.Floor(total / numberOfPayments * 100m) / 100m;
        if (regular <= 0m)
        {
            throw new LoanScheduleItemAmountMustBePositiveException();
        }

        var entries = new List<LoanScheduleEntry>(numberOfPayments);
        for (var i = 0; i < numberOfPayments; i++)
        {
            var amount = i == numberOfPayments - 1 ? total - regular * (numberOfPayments - 1) : regular;
            entries.Add(new LoanScheduleEntry(firstPaymentDate.AddMonths(i), amount));
        }

        return entries;
    }
}
