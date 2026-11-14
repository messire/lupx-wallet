using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Domain;

/// <summary>
/// PlannedTransaction events (ADR-0010 §6, §8). Audit subscribes to all of them; plan-level events use
/// the ChangeTracker diff, occurrence-level events carry their own old/new values because overrides and
/// executions are child rows that the root diff does not see.
/// </summary>
public sealed record PlannedTransactionCreated(PlannedTransactionId PlannedTransactionId) : DomainEvent;

public sealed record PlannedTransactionUpdated(PlannedTransactionId PlannedTransactionId) : DomainEvent;

public sealed record PlannedTransactionFinished(PlannedTransactionId PlannedTransactionId) : DomainEvent;

public sealed record PlannedTransactionDeleted(PlannedTransactionId PlannedTransactionId) : DomainEvent;

public sealed record PlannedOccurrenceSkipped(PlannedTransactionId PlannedTransactionId, DateOnly OccurrenceDate) : DomainEvent;

public sealed record PlannedOccurrenceUnskipped(PlannedTransactionId PlannedTransactionId, DateOnly OccurrenceDate) : DomainEvent;

/// <param name="OldEffectiveDate">Effective date before the move.</param>
/// <param name="NewEffectiveDate">Effective date after the move (the original date when the move is undone).</param>
public sealed record PlannedOccurrenceMoved(
    PlannedTransactionId PlannedTransactionId, DateOnly OccurrenceDate, DateOnly OldEffectiveDate, DateOnly NewEffectiveDate) : DomainEvent;

/// <param name="OldAmount">Overridden amount before; null — the plan amount was used.</param>
/// <param name="NewAmount">Overridden amount after; null — back to the plan amount.</param>
public sealed record PlannedOccurrenceAmountChanged(
    PlannedTransactionId PlannedTransactionId, DateOnly OccurrenceDate, decimal? OldAmount, decimal? NewAmount) : DomainEvent;

public sealed record PlannedOccurrenceExecuted(
    PlannedTransactionId PlannedTransactionId, DateOnly OccurrenceDate, OperationId OperationId, decimal Amount) : DomainEvent;
