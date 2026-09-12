namespace LupexWallet.Operations.Domain;

/// <summary>
/// Local mirror of ReferenceData.OperationBehaviorKind.Codes for validation inside
/// Operations.Domain — Domain itself does not depend on ReferenceData; Operations.Application
/// maps the behavior code (a string from IOperationTypeLookup) to this value before calling
/// the Operation.Create/Update factory (ADR-0007).
/// </summary>
public enum OperationEffectKind
{
    Income,
    Expense,
    Transfer,
    Adjustment,
}

/// <summary>Manual adjustment input mode (decided, Q3).</summary>
public enum AdjustmentMode
{
    Absolute,
    Delta,
}
