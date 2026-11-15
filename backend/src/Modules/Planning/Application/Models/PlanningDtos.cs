using LupexWallet.Planning.Domain;

namespace LupexWallet.Planning.Application;

public sealed record RecurrenceDto(RecurrenceFrequency Frequency, int Interval, DateOnly? Until, int? Count);

public sealed record PlannedTransactionDto(
    Guid Id,
    Guid OperationTypeId,
    PlannedDirection Direction,
    decimal Amount,
    Guid CurrencyId,
    Guid? WalletId,
    string? Description,
    DateOnly StartDate,
    RecurrenceDto? Recurrence,
    PlannedTransactionStatus Status,
    bool HasExecutions,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? FinishedAt)
{
    public static PlannedTransactionDto FromDomain(PlannedTransaction plan) => new(
        plan.Id.Value,
        plan.OperationTypeId.Value,
        plan.Direction,
        plan.Amount,
        plan.CurrencyId.Value,
        plan.WalletId?.Value,
        plan.Description,
        plan.StartDate,
        plan.Recurrence is { } rule ? new RecurrenceDto(rule.Frequency, rule.Interval, rule.Until, rule.Count) : null,
        plan.Status,
        plan.HasExecutions,
        plan.CreatedAt,
        plan.UpdatedAt,
        plan.FinishedAt);
}

public sealed record PlannedTransactionPageDto(IReadOnlyList<PlannedTransactionDto> Data, string? NextCursor, bool HasMore);

public sealed record PlannedOccurrenceExecutionDto(Guid OperationId, decimal Amount, bool IsOperationDeleted, DateTimeOffset ExecutedAt);

/// <summary>One computed occurrence with the plan fields a client needs to show it.</summary>
public sealed record PlannedOccurrenceDto(
    Guid PlannedTransactionId,
    DateOnly OccurrenceDate,
    DateOnly EffectiveDate,
    Guid OperationTypeId,
    PlannedDirection Direction,
    Guid CurrencyId,
    Guid? WalletId,
    string? Description,
    decimal PlannedAmount,
    decimal ExecutedAmount,
    decimal ExpectedAmount,
    PlannedOccurrenceStatus Status,
    bool IsOverdue,
    bool IsMoved,
    bool IsAmountChanged,
    IReadOnlyList<PlannedOccurrenceExecutionDto> Executions)
{
    public static PlannedOccurrenceDto From(PlannedTransaction plan, PlannedOccurrence occurrence) => new(
        plan.Id.Value,
        occurrence.OccurrenceDate,
        occurrence.EffectiveDate,
        plan.OperationTypeId.Value,
        plan.Direction,
        plan.CurrencyId.Value,
        plan.WalletId?.Value,
        plan.Description,
        occurrence.PlannedAmount,
        occurrence.ExecutedAmount,
        occurrence.ExpectedAmount,
        occurrence.Status,
        occurrence.IsOverdue,
        occurrence.IsMoved,
        occurrence.IsAmountChanged,
        occurrence.Executions
            .Select(e => new PlannedOccurrenceExecutionDto(e.OperationId.Value, e.Amount, e.IsOperationDeleted, e.ExecutedAt))
            .ToList());
}

public sealed record PlannedOccurrenceListDto(DateOnly From, DateOnly To, IReadOnlyList<PlannedOccurrenceDto> Data);

public sealed record PlannedOccurrenceExecutionResultDto(PlannedOccurrenceDto Occurrence, Guid OperationId);
