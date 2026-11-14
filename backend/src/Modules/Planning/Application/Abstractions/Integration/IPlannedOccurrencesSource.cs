using LupexWallet.SharedKernel;

namespace LupexWallet.Planning.Application;

/// <summary>
/// Read-only port published by Planning for Reporting (ADR-0010 §7: cash-flow forecast, upcoming
/// payments). Returns only what is still expected: occurrences of active plans that are not skipped
/// and not fully executed, with the remaining amount. Implemented in Planning.Infrastructure.
/// </summary>
public interface IPlannedOccurrencesSource
{
    /// <summary>
    /// Expected occurrences with <paramref name="from"/> ≤ EffectiveDate ≤ <paramref name="to"/>, ordered by
    /// effective date. Overdue ones (EffectiveDate before today) are included when <paramref name="from"/> reaches them.
    /// </summary>
    Task<IReadOnlyList<ExpectedPlannedOccurrence>> ListExpectedAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <param name="SignedAmount">Remaining expected amount: positive for income, negative for expense.</param>
/// <param name="WalletId">Default wallet of the plan; null when the plan has none.</param>
public sealed record ExpectedPlannedOccurrence(
    PlannedTransactionId PlannedTransactionId,
    DateOnly OccurrenceDate,
    DateOnly EffectiveDate,
    OperationTypeId OperationTypeId,
    CurrencyId CurrencyId,
    WalletId? WalletId,
    decimal SignedAmount,
    bool IsOverdue);
