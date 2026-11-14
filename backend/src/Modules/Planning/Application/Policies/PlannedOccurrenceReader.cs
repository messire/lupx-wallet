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
        var operationIds = plans.SelectMany(p => p.Executions).Select(e => e.OperationId).Distinct().ToList();
        if (operationIds.Count == 0)
        {
            return new Dictionary<OperationId, ExecutionOperationFact>();
        }

        var facts = await operationFactsLookup.GetByIdsAsync(operationIds, cancellationToken);
        return facts.ToDictionary(f => f.OperationId, f => new ExecutionOperationFact(f.Amount, f.IsDeleted));
    }
}
