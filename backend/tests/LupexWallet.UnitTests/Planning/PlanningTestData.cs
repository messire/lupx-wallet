using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.UnitTests.Planning;

internal static class PlanningTestData
{
    public static readonly CurrencyId Currency = new(Guid.NewGuid());
    public static readonly IReadOnlyDictionary<OperationId, ExecutionOperationFact> NoFacts =
        new Dictionary<OperationId, ExecutionOperationFact>();

    public static DateOnly D(int y, int m, int d) => new(y, m, d);

    public static PlannedTransaction Monthly(
        DateOnly start, decimal amount = 100m, PlannedDirection direction = PlannedDirection.Expense, WalletId? walletId = null) =>
        PlannedTransaction.Create(
            PlannedTransactionId.New(), new OperationTypeId(Guid.NewGuid()), direction, amount, Currency, walletId,
            "Аренда", start, RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, null));

    public static PlannedTransaction OneOff(DateOnly date, decimal amount = 100m) =>
        PlannedTransaction.Create(
            PlannedTransactionId.New(), new OperationTypeId(Guid.NewGuid()), PlannedDirection.Income, amount, Currency, null,
            null, date, null);

    public static PlannedOccurrence Current(PlannedTransaction plan, DateOnly date, DateOnly? today = null,
        IReadOnlyDictionary<OperationId, ExecutionOperationFact>? facts = null) =>
        OccurrenceProjector.ProjectOne(plan, date, today ?? date, facts ?? NoFacts);

    public static OperationId Execute(PlannedTransaction plan, DateOnly date, decimal amount,
        IReadOnlyDictionary<OperationId, ExecutionOperationFact>? facts = null)
    {
        var operationId = OperationId.New();
        plan.RecordExecution(Current(plan, date, facts: facts), operationId, amount);
        return operationId;
    }
}
