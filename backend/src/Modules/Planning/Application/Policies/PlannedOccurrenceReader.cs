using LupexWallet.Operations.Application;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Application;

/// <summary>
/// Computes occurrences of plans with the current facts of their execution operations, read through
/// Operations' IOperationFactsLookup (soft-deleted included). Deriving executed amounts on read keeps
/// deletion, restoration and edits of the operation in effect without event syncing (ADR-0010 §6).
/// </summary>
public sealed class PlannedOccurrenceReader(IOperationFactsLookup operationFactsLookup)
{
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public async Task<IReadOnlyList<(PlannedTransaction Plan, PlannedOccurrence Occurrence)>> ProjectAsync(
        IReadOnlyCollection<PlannedTransaction> plans, DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var facts = await LoadFactsAsync(plans, cancellationToken);
        var today = Today;

        return plans
            .SelectMany(plan => OccurrenceProjector.Project(plan, from, to, today, facts).Select(o => (plan, o)))
            .OrderBy(x => x.o.EffectiveDate)
            .ThenBy(x => x.plan.CreatedAt)
            .ThenBy(x => x.o.OccurrenceDate)
            .ToList();
    }

    public async Task<PlannedOccurrence> ProjectOneAsync(PlannedTransaction plan, DateOnly occurrenceDate, CancellationToken cancellationToken)
    {
        if (!plan.IsOccurrence(occurrenceDate))
        {
            throw new PlannedOccurrenceNotFoundException(occurrenceDate);
        }

        var facts = await LoadFactsAsync([plan], cancellationToken);
        return OccurrenceProjector.ProjectOne(plan, occurrenceDate, Today, facts);
    }

    private async Task<IReadOnlyDictionary<OperationId, ExecutionOperationFact>> LoadFactsAsync(
        IEnumerable<PlannedTransaction> plans, CancellationToken cancellationToken)
    {
        var planByOperation = plans
            .SelectMany(p => p.Executions.Select(e => (e.OperationId, Plan: p)))
            .GroupBy(x => x.OperationId)
            .ToDictionary(g => g.Key, g => g.First().Plan);
        if (planByOperation.Count == 0)
        {
            return new Dictionary<OperationId, ExecutionOperationFact>();
        }

        var facts = await operationFactsLookup.GetByIdsAsync(planByOperation.Keys.ToList(), cancellationToken);

        // An operation edited after execution into another currency or the opposite direction no longer
        // fulfils the plan: it is treated like a deleted one, so the occurrence becomes expected again.
        return facts.ToDictionary(
            f => f.OperationId,
            f => new ExecutionOperationFact(f.Amount, f.IsDeleted || !FulfilsPlan(f, planByOperation[f.OperationId])));
    }

    private static bool FulfilsPlan(OperationFact fact, PlannedTransaction plan) =>
        !fact.IsCorrection
        && !fact.IsTransferLeg
        && fact.CurrencyId == plan.CurrencyId
        && (plan.Direction == PlannedDirection.Expense ? fact.AppliedDelta < 0 : fact.AppliedDelta > 0);
}
