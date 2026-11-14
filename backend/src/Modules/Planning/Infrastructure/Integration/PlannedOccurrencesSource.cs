using LupexWallet.Planning.Application;
using LupexWallet.Planning.Domain;

namespace LupexWallet.Planning.Infrastructure;

/// <summary>IPlannedOccurrencesSource for Reporting: the same projection as GET /planned-occurrences, expected parts only.</summary>
public sealed class PlannedOccurrencesSource(IPlannedTransactionRepository repository, PlannedOccurrenceReader reader)
    : IPlannedOccurrencesSource
{
    public async Task<IReadOnlyList<ExpectedPlannedOccurrence>> ListExpectedAsync(
        DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var plans = (await repository.ListAllAsync(onlyId: null, cancellationToken)).Where(p => p.IsActive).ToList();
        var occurrences = await reader.ProjectAsync(plans, from, to, cancellationToken);

        return occurrences
            .Where(x => x.Occurrence.ExpectedAmount > 0)
            .Select(x => new ExpectedPlannedOccurrence(
                x.Plan.Id,
                x.Occurrence.OccurrenceDate,
                x.Occurrence.EffectiveDate,
                x.Plan.OperationTypeId,
                x.Plan.CurrencyId,
                x.Plan.WalletId,
                x.Plan.Direction == PlannedDirection.Income ? x.Occurrence.ExpectedAmount : -x.Occurrence.ExpectedAmount,
                x.Occurrence.IsOverdue))
            .ToList();
    }
}
