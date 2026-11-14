using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;

namespace LupexWallet.Debts.Application;

/// <summary>
/// Operation facts behind linked repayments/payments, read through Operations' port
/// (ADR-0010 §3). Amounts and statuses are derived from them on every read instead of being
/// synced by events: a soft-deleted operation stops counting, a restored one counts again, an
/// edited amount is picked up as is.
/// </summary>
public sealed class PaymentFacts(IReadOnlyDictionary<OperationId, OperationFact> facts)
{
    public static readonly PaymentFacts Empty = new(new Dictionary<OperationId, OperationFact>());

    public OperationFact? Find(OperationId operationId) => facts.GetValueOrDefault(operationId);

    /// <summary>
    /// A payment counts while its operation exists, is not deleted, is still an expense
    /// (negative effect) and is still in the obligation's currency.
    /// </summary>
    public bool IsCounted(OperationId operationId, CurrencyId currencyId) =>
        Find(operationId) is { IsDeleted: false } fact && fact.CurrencyId == currencyId && fact.AppliedDelta < 0m;

    public decimal CountedAmount(OperationId operationId, CurrencyId currencyId) =>
        IsCounted(operationId, currencyId) ? Find(operationId)!.Amount : 0m;

    public decimal CountedTotal(IEnumerable<OperationId> operationIds, CurrencyId currencyId) =>
        operationIds.Sum(id => CountedAmount(id, currencyId));
}

public sealed class PaymentFactsReader(IOperationFactsLookup lookup)
{
    public async Task<PaymentFacts> ReadAsync(IEnumerable<OperationId> operationIds, CancellationToken cancellationToken)
    {
        var ids = operationIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return PaymentFacts.Empty;
        }

        var facts = await lookup.GetByIdsAsync(ids, cancellationToken);
        return new PaymentFacts(facts.ToDictionary(x => x.OperationId));
    }
}
