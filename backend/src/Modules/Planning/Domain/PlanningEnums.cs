namespace LupexWallet.Planning.Domain;

/// <summary>
/// Direction of a planned transaction, taken from the behavior of its operation type (ADR-0010 §6).
/// Local mirror of the ReferenceData behavior codes Income/Expense; transfers are out of scope.
/// </summary>
public enum PlannedDirection
{
    Income,
    Expense,
}

/// <summary>How often a planned transaction repeats (ADR-0010 §6, RecurrenceRule).</summary>
public enum RecurrenceFrequency
{
    Daily,
    Weekly,
    Monthly,
    Yearly,
}

public enum PlannedTransactionStatus
{
    Active,
    Finished,
}

/// <summary>Computed state of one occurrence; never stored.</summary>
public enum PlannedOccurrenceStatus
{
    Expected,
    PartiallyExecuted,
    Executed,
    Skipped,
}
