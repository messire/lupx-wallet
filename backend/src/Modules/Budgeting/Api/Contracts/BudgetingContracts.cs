namespace LupexWallet.Budgeting.Api;

// DTOs of docs/api/openapi.yaml, tag Budgeting. Money amounts are strings (ADR-0005).

public sealed record BudgetingPageMeta(string? NextCursor, bool HasMore);

public sealed record ExpenseCategoryResponse(Guid Id, string Name, bool IsActive, IReadOnlyList<Guid> OperationTypeIds);
public sealed record ExpenseCategoryCreateRequest(string Name, IReadOnlyList<Guid>? OperationTypeIds);
public sealed record ExpenseCategoryUpdateRequest(string? Name, IReadOnlyList<Guid>? OperationTypeIds);
public sealed record ExpenseCategoryPageResponse(IReadOnlyList<ExpenseCategoryResponse> Data, BudgetingPageMeta Pagination);

public sealed record CategoryLimitResponse(Guid ExpenseCategoryId, string Limit);
public sealed record BudgetResponse(
    Guid Id, string Month, Guid CurrencyId, IReadOnlyList<CategoryLimitResponse> Limits, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record BudgetCreateRequest(string Month, Guid CurrencyId);
/// <summary>Web JSON defaults read the decimal from a string as well as from a number.</summary>
public sealed record BudgetLimitRequest(decimal? Limit);
public sealed record BudgetPageResponse(IReadOnlyList<BudgetResponse> Data, BudgetingPageMeta Pagination);

public sealed record CategoryExecutionResponse(
    Guid ExpenseCategoryId, string Name, bool IsActive, string Limit, string Actual, string Remaining, bool IsOverspent);
public sealed record UnbudgetedExecutionResponse(string Actual);
public sealed record BudgetTotalsResponse(string Limit, string BudgetedActual, string Remaining, string Actual);
public sealed record AppliedRateResponse(Guid FromCurrencyId, DateOnly OperationDate, DateOnly RateDate, string Rate, bool IsFallback);
public sealed record ExcludedOperationResponse(Guid OperationId, Guid CurrencyId, DateOnly OperationDate, string Amount, string Reason);
public sealed record BudgetExecutionResponse(
    Guid BudgetId,
    string Month,
    Guid CurrencyId,
    IReadOnlyList<CategoryExecutionResponse> Categories,
    UnbudgetedExecutionResponse Unbudgeted,
    BudgetTotalsResponse Totals,
    string RateSource,
    IReadOnlyList<AppliedRateResponse> AppliedRates,
    IReadOnlyList<ExcludedOperationResponse> ExcludedOperations);
