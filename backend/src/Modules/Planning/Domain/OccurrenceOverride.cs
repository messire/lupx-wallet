namespace LupexWallet.Planning.Domain;

/// <summary>
/// Stored deviation of one occurrence from its plan (ADR-0010 §6), keyed by the original occurrence
/// date: skipped, moved to another date and/or a changed amount. A row with no deviation left is removed.
/// </summary>
public sealed class OccurrenceOverride
{
    public DateOnly OccurrenceDate { get; private set; }
    public bool IsSkipped { get; private set; }
    public DateOnly? MovedToDate { get; private set; }
    public decimal? Amount { get; private set; }

    private OccurrenceOverride()
    {
        // Только для EF Core.
    }

    internal OccurrenceOverride(DateOnly occurrenceDate) => OccurrenceDate = occurrenceDate;

    internal bool IsEmpty => !IsSkipped && MovedToDate is null && Amount is null;

    internal void SetSkipped(bool isSkipped) => IsSkipped = isSkipped;

    internal void SetMovedTo(DateOnly? movedToDate) => MovedToDate = movedToDate;

    internal void SetAmount(decimal? amount) => Amount = amount;
}
