using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Domain;

/// <summary>
/// Link between an occurrence and the real operation that executed it (ADR-0010 §6). Amount is the
/// amount at execution time; reads prefer the current operation facts, so editing, soft-deleting or
/// restoring the operation is reflected without syncing (see OccurrenceProjector).
/// </summary>
public sealed class OccurrenceExecution
{
    public Guid Id { get; private set; }
    public DateOnly OccurrenceDate { get; private set; }
    public OperationId OperationId { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset ExecutedAt { get; private set; }

    private OccurrenceExecution()
    {
        // Только для EF Core.
    }

    internal OccurrenceExecution(DateOnly occurrenceDate, OperationId operationId, decimal amount, DateTimeOffset executedAt)
    {
        Id = Guid.NewGuid();
        OccurrenceDate = occurrenceDate;
        OperationId = operationId;
        Amount = amount;
        ExecutedAt = executedAt;
    }
}
