namespace LupexWallet.Operations.Domain;

/// <summary>
/// Local mirror of ReferenceData.OperationBehaviorKind.Codes for validation inside
/// Operations.Domain — Domain itself does not depend on ReferenceData; Operations.Application
/// maps the behavior code (a string from IOperationTypeLookup) to this value before calling
/// the Operation.Create/Update factory (ADR-0001).
/// </summary>
public enum OperationEffectKind
{
    Income,
    Expense,
    Transfer,
    Adjustment,
}

/// <summary>
/// Manual adjustment input mode. Only a change by an amount: stating the new balance for a date
/// is a separate action that creates a balance correction (ADR-0003, §6).
/// </summary>
public enum AdjustmentMode
{
    Delta,
}
