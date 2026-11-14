namespace LupexWallet.Debts.Api;

// DTOs per docs/api/openapi.yaml (tag Debts: Creditor, Debt, Loan, DebtSummary and their requests/pages).
// Amounts in responses are strings (ADR-0005); requests accept strings or numbers (web JSON defaults).

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record CreditorResponse(Guid Id, string Name, bool IsActive);
public sealed record CreditorCreateRequest(string Name);
public sealed record CreditorUpdateRequest(string Name);
public sealed record CreditorPageResponse(IReadOnlyList<CreditorResponse> Data, CursorPageMeta Pagination);

public sealed record PaymentResponse(
    Guid Id, Guid OperationId, Guid WalletId, MoneyResponse Amount, DateOnly PaymentDate,
    bool IsCounted, bool IsOperationDeleted, DateTimeOffset CreatedAt);

/// <summary>Nullable fields so a missing value is answered with 400, not read as zero/default.</summary>
public sealed record PaymentCreateRequest(Guid? WalletId, Guid? OperationTypeId, decimal? Amount, DateOnly? PaymentDate);

public sealed record DebtResponse(
    Guid Id, Guid CreditorId, string Name, Guid CurrencyId,
    MoneyResponse OriginalAmount, MoneyResponse RepaidAmount, MoneyResponse RemainingAmount,
    DateOnly StartDate, string Status, DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt,
    IReadOnlyList<PaymentResponse> Repayments);

public sealed record DebtCreateRequest(Guid? CreditorId, string? Name, Guid? CurrencyId, decimal? OriginalAmount, DateOnly? StartDate);
public sealed record DebtUpdateRequest(Guid? CreditorId, string? Name, decimal? OriginalAmount, DateOnly? StartDate);
public sealed record DebtPageResponse(IReadOnlyList<DebtResponse> Data, CursorPageMeta Pagination);

public sealed record DebtCreditorSummaryResponse(Guid CreditorId, string CreditorName, MoneyResponse RemainingAmount, int DebtCount);
public sealed record DebtCurrencySummaryResponse(
    Guid CurrencyId, MoneyResponse TotalRemainingAmount, int DebtCount, IReadOnlyList<DebtCreditorSummaryResponse> Creditors);
public sealed record DebtSummaryResponse(IReadOnlyList<DebtCurrencySummaryResponse> Currencies);

public sealed record LoanScheduleItemResponse(
    DateOnly DueDate, MoneyResponse Amount, MoneyResponse PaidAmount, MoneyResponse OutstandingAmount, string Status);

public sealed record LoanResponse(
    Guid Id, Guid CreditorId, string Name, Guid CurrencyId,
    MoneyResponse AmountToRepay, MoneyResponse PaidAmount, MoneyResponse RemainingAmount, MoneyResponse ScheduledAmount,
    DateOnly StartDate, string Status, LoanScheduleItemResponse? NextPayment,
    IReadOnlyList<LoanScheduleItemResponse> Schedule, IReadOnlyList<PaymentResponse> Payments,
    DateTimeOffset? CancelledAt, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record LoanCreateRequest(Guid? CreditorId, string? Name, Guid? CurrencyId, decimal? AmountToRepay, DateOnly? StartDate);
public sealed record LoanUpdateRequest(Guid? CreditorId, string? Name, decimal? AmountToRepay, DateOnly? StartDate);
public sealed record LoanPageResponse(IReadOnlyList<LoanResponse> Data, CursorPageMeta Pagination);

public sealed record LoanScheduleItemRequest(DateOnly? DueDate, decimal? Amount);
public sealed record LoanScheduleReplaceRequest(IReadOnlyList<LoanScheduleItemRequest>? Items);
public sealed record LoanScheduleGenerateRequest(DateOnly? FirstPaymentDate, int? NumberOfPayments);
