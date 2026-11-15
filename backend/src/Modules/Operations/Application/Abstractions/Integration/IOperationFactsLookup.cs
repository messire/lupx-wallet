using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Read-only port published by Operations (ADR-0010): facts about real operations that other
/// modules link to (repayments, plan executions) or aggregate (budget execution).
/// </summary>
public interface IOperationFactsLookup
{
    /// <summary>Operations by id, including soft-deleted ones; unknown ids are omitted.</summary>
    Task<IReadOnlyList<OperationFact>> GetByIdsAsync(
        IReadOnlyCollection<OperationId> operationIds, CancellationToken cancellationToken);

    /// <summary>
    /// Non-deleted user operations (no balance corrections, no transfer legs) with
    /// <paramref name="from"/> ≤ OperationDate ≤ <paramref name="to"/>.
    /// </summary>
    Task<IReadOnlyList<OperationFact>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}

/// <param name="Amount">Unsigned amount as entered.</param>
/// <param name="AppliedDelta">Signed effect on the wallet balance (negative for an expense).</param>
public sealed record OperationFact(
    OperationId OperationId,
    WalletId WalletId,
    OperationTypeId? OperationTypeId,
    CurrencyId CurrencyId,
    decimal Amount,
    decimal AppliedDelta,
    DateOnly OperationDate,
    bool IsTransferLeg,
    bool IsCorrection,
    bool IsDeleted);
