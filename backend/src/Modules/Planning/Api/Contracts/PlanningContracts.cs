namespace LupexWallet.Planning.Api;

// DTO по контракту docs/api/openapi.yaml (тег Planning; схемы PlannedTransaction, PlannedOccurrence и др.).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

/// <summary>Interval defaults to 1; Until and Count are mutually exclusive.</summary>
public sealed record RecurrenceContract(string Frequency, int? Interval, DateOnly? Until, int? Count);

/// <summary>Amount is nullable so that a missing field is answered with 400 instead of a zero amount.</summary>
public sealed record PlannedTransactionWriteRequest(
    Guid OperationTypeId,
    decimal? Amount,
    Guid CurrencyId,
    Guid? WalletId,
    string? Description,
    DateOnly StartDate,
    RecurrenceContract? Recurrence);

public sealed record PlannedTransactionResponse(
    Guid Id,
    Guid OperationTypeId,
    string Direction,
    MoneyResponse Amount,
    Guid? WalletId,
    string? Description,
    DateOnly StartDate,
    RecurrenceContract? Recurrence,
    string Status,
    bool HasExecutions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt);

public sealed record PlannedTransactionPageResponse(IReadOnlyList<PlannedTransactionResponse> Data, CursorPageMeta Pagination);

public sealed record PlannedOccurrenceExecutionResponse(Guid OperationId, MoneyResponse Amount, bool IsOperationDeleted, DateTimeOffset ExecutedAt);

public sealed record PlannedOccurrenceResponse(
    Guid PlannedTransactionId,
    DateOnly OccurrenceDate,
    DateOnly EffectiveDate,
    Guid OperationTypeId,
    string Direction,
    Guid? WalletId,
    string? Description,
    MoneyResponse PlannedAmount,
    MoneyResponse ExecutedAmount,
    MoneyResponse ExpectedAmount,
    string Status,
    bool IsOverdue,
    bool IsMoved,
    bool IsAmountChanged,
    IReadOnlyList<PlannedOccurrenceExecutionResponse> Executions);

public sealed record PlannedOccurrenceListResponse(DateOnly From, DateOnly To, IReadOnlyList<PlannedOccurrenceResponse> Data);

public sealed record PlannedOccurrenceMoveRequest(DateOnly? Date);

/// <summary>Amount null returns the occurrence to the plan amount.</summary>
public sealed record PlannedOccurrenceAmountRequest(decimal? Amount);

/// <summary>All fields optional: the plan wallet, the remaining amount and the effective date are used by default.</summary>
public sealed record PlannedOccurrenceExecuteRequest(Guid? WalletId, decimal? Amount, DateOnly? OperationDate);

public sealed record PlannedOccurrenceExecutionResultResponse(PlannedOccurrenceResponse Occurrence, Guid OperationId);
