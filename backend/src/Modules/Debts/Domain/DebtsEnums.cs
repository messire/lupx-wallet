namespace LupexWallet.Debts.Domain;

/// <summary>Status of a debt or a loan (ADR-0010 §3). Repaid is derived from counted payments, not stored.</summary>
public enum ObligationStatus
{
    Active,
    Repaid,
    Cancelled,
}

/// <summary>Computed status of a loan schedule item (ADR-0010 §3).</summary>
public enum LoanScheduleItemStatus
{
    /// <summary>Fully covered by counted payments.</summary>
    Paid,

    /// <summary>Partly covered, due date is today or later.</summary>
    Partial,

    /// <summary>Due date is before today and the item is not fully covered.</summary>
    Overdue,

    /// <summary>Not covered at all, due date is today or later.</summary>
    Expected,
}
