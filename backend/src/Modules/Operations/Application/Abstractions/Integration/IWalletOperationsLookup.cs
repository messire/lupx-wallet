using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Narrow read-only port published by Operations for BalanceHistory (ADR-0003, mirrors
/// ADR-0001) — signed daily deltas (including both transfer legs, stored as plain Operation
/// rows with AppliedDelta already set, see Transfer.Create) and balance corrections (ADR-0003, §6),
/// which are rows of the same table with the system-calculated amount, needed to reconstruct a wallet's
/// balance on an arbitrary date without recomputing operation-type behavior.
/// </summary>
public interface IWalletOperationsLookup
{
    /// <summary>Deltas grouped by OperationDate, ascending, for all non-deleted operations (soft-deleted ones are excluded, ADR-0002 §3) up to and including <paramref name="throughDate"/>.</summary>
    Task<IReadOnlyList<DailyOperationsDelta>> GetDailyDeltasUpToAsync(
        WalletId walletId, DateOnly throughDate, CancellationToken cancellationToken);
}

public sealed record DailyOperationsDelta(DateOnly OperationDate, decimal DeltaAmount);
