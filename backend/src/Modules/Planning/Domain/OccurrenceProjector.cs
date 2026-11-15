using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Domain;

/// <summary>Current facts of an execution's operation: its amount and whether it is soft-deleted.</summary>
public sealed record ExecutionOperationFact(decimal Amount, bool IsDeleted);

/// <param name="Amount">Amount counted for the occurrence: the operation amount, 0 when it is soft-deleted.</param>
public sealed record PlannedOccurrenceExecution(OperationId OperationId, decimal Amount, bool IsOperationDeleted, DateTimeOffset ExecutedAt);

/// <summary>
/// Computed occurrence (never stored). <see cref="ExpectedAmount"/> is what is still expected: the
/// planned amount minus executions, 0 when skipped, fully executed or the plan is finished.
/// </summary>
public sealed record PlannedOccurrence(
    PlannedTransactionId PlannedTransactionId,
    DateOnly OccurrenceDate,
    DateOnly EffectiveDate,
    decimal PlannedAmount,
    decimal ExecutedAmount,
    decimal ExpectedAmount,
    PlannedOccurrenceStatus Status,
    bool IsOverdue,
    bool IsMoved,
    bool IsAmountChanged,
    IReadOnlyList<PlannedOccurrenceExecution> Executions);

/// <summary>
/// Pure domain service: turns a plan, its stored deviations and the current facts of the execution
/// operations into occurrences (ADR-0010 §6). An occurrence belongs to a range by its effective date
/// (after a move), so a move can bring an occurrence into or out of the range.
/// A finished plan expects nothing: only occurrences with counted executions are returned for it.
/// </summary>
public static class OccurrenceProjector
{
    /// <param name="operationFacts">Facts by operation id; a missing id falls back to the stored execution amount.</param>
    public static IReadOnlyList<PlannedOccurrence> Project(
        PlannedTransaction plan,
        DateOnly from,
        DateOnly to,
        DateOnly today,
        IReadOnlyDictionary<OperationId, ExecutionOperationFact> operationFacts)
    {
        var recurrence = plan.Recurrence;
        var originals = new SortedSet<DateOnly>(OccurrenceSchedule.Expand(plan.StartDate, recurrence, from, to));

        // Moved in from outside the range.
        foreach (var moved in plan.Overrides)
        {
            if (moved.MovedToDate is { } target && target >= from && target <= to
                && OccurrenceSchedule.IsOccurrence(plan.StartDate, recurrence, moved.OccurrenceDate))
            {
                originals.Add(moved.OccurrenceDate);
            }
        }

        var result = new List<PlannedOccurrence>();
        foreach (var date in originals)
        {
            var occurrence = Build(plan, date, today, operationFacts);
            if (occurrence.EffectiveDate < from || occurrence.EffectiveDate > to)
            {
                continue;
            }

            if (!plan.IsActive && occurrence.ExecutedAmount <= 0)
            {
                continue;
            }

            result.Add(occurrence);
        }

        return result.OrderBy(o => o.EffectiveDate).ThenBy(o => o.OccurrenceDate).ToList();
    }

    /// <summary>One occurrence by its original date; throws when the date is not in the schedule.</summary>
    public static PlannedOccurrence ProjectOne(
        PlannedTransaction plan,
        DateOnly occurrenceDate,
        DateOnly today,
        IReadOnlyDictionary<OperationId, ExecutionOperationFact> operationFacts)
    {
        if (!plan.IsOccurrence(occurrenceDate))
        {
            throw new PlannedOccurrenceNotFoundException(occurrenceDate);
        }

        return Build(plan, occurrenceDate, today, operationFacts);
    }

    private static PlannedOccurrence Build(
        PlannedTransaction plan,
        DateOnly occurrenceDate,
        DateOnly today,
        IReadOnlyDictionary<OperationId, ExecutionOperationFact> operationFacts)
    {
        var deviation = plan.FindOverride(occurrenceDate);
        var effectiveDate = deviation?.MovedToDate ?? occurrenceDate;
        var plannedAmount = deviation?.Amount ?? plan.Amount;

        var executions = plan.Executions
            .Where(e => e.OccurrenceDate == occurrenceDate)
            .OrderBy(e => e.ExecutedAt)
            .Select(e =>
            {
                var fact = operationFacts.GetValueOrDefault(e.OperationId);
                var isDeleted = fact?.IsDeleted ?? false;
                var amount = isDeleted ? 0m : fact?.Amount ?? e.Amount;
                return new PlannedOccurrenceExecution(e.OperationId, amount, isDeleted, e.ExecutedAt);
            })
            .ToList();

        var executedAmount = executions.Sum(e => e.Amount);

        var status = deviation?.IsSkipped == true ? PlannedOccurrenceStatus.Skipped
            : executedAmount >= plannedAmount ? PlannedOccurrenceStatus.Executed
            : executedAmount > 0 ? PlannedOccurrenceStatus.PartiallyExecuted
            : PlannedOccurrenceStatus.Expected;

        var isPending = plan.IsActive && status is PlannedOccurrenceStatus.Expected or PlannedOccurrenceStatus.PartiallyExecuted;
        var expectedAmount = isPending ? plannedAmount - executedAmount : 0m;

        return new PlannedOccurrence(
            plan.Id,
            occurrenceDate,
            effectiveDate,
            plannedAmount,
            executedAmount,
            expectedAmount,
            status,
            IsOverdue: isPending && effectiveDate < today,
            IsMoved: deviation?.MovedToDate is not null,
            IsAmountChanged: deviation?.Amount is not null,
            executions);
    }
}
