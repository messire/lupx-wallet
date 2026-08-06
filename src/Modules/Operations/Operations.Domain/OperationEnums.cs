namespace LupexWallet.Operations.Domain;

/// <summary>
/// Локальное отражение ReferenceData.OperationBehaviorKind.Codes для валидации внутри
/// Operations.Domain — сам Domain не зависит от ReferenceData; Operations.Application
/// переводит код поведения (строку из IOperationTypeLookup) в это значение перед
/// вызовом фабрики Operation.Create/Update (ADR-0007).
/// </summary>
public enum OperationEffectKind
{
    Income,
    Expense,
    Transfer,
    Adjustment,
}

/// <summary>Способ ввода ручной корректировки (решено, Q3).</summary>
public enum AdjustmentMode
{
    Absolute,
    Delta,
}
