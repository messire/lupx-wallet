namespace LupexWallet.SharedKernel;

// Строго типизированные идентификаторы агрегатов (ddd-model.md, "Общие соглашения").
// Живут в SharedKernel, а не в Domain своего модуля, чтобы другие модули могли
// ссылаться на чужой агрегат по Id, не завися от его модуля (high-level-architecture.md, §2).

public readonly record struct WalletId(Guid Value)
{
    public static WalletId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct WalletTypeId(Guid Value)
{
    public static WalletTypeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct OperationTypeId(Guid Value)
{
    public static OperationTypeId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct OperationBehaviorKindId(Guid Value)
{
    public static OperationBehaviorKindId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct CurrencyId(Guid Value)
{
    public static CurrencyId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct OperationId(Guid Value)
{
    public static OperationId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct TransferId(Guid Value)
{
    public static TransferId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct AuditEntryId(Guid Value)
{
    public static AuditEntryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

// ---- Budgeting (ADR-0010 §5) ----

public readonly record struct ExpenseCategoryId(Guid Value)
{
    public static ExpenseCategoryId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct BudgetId(Guid Value)
{
    public static BudgetId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

// ---- Funds (ADR-0010 §4) ----

public readonly record struct FundId(Guid Value)
{
    public static FundId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

// ---- Debts (ADR-0010 §3) ----

public readonly record struct CreditorId(Guid Value)
{
    public static CreditorId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct DebtId(Guid Value)
{
    public static DebtId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}

public readonly record struct LoanId(Guid Value)
{
    public static LoanId New() => new(Guid.NewGuid());
    public override string ToString() => Value.ToString();
}
