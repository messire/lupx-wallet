using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Narrow read-only port published by Operations for BalanceHistory (ADR-0008, mirrors
/// ADR-0007) — signed daily deltas (including both transfer legs, stored as plain Operation
/// rows with AppliedDelta already set, see Transfer.Create) needed to reconstruct a wallet's
/// balance on an arbitrary date without recomputing operation-type behavior.
/// </summary>
public interface IWalletOperationsLookup
{
    /// <summary>Deltas grouped by OperationDate, ascending, for all operations up to and including <paramref name="throughDate"/>.</summary>
    Task<IReadOnlyList<DailyOperationsDelta>> GetDailyDeltasUpToAsync(
        WalletId walletId, DateOnly throughDate, CancellationToken cancellationToken);
}

public sealed record DailyOperationsDelta(DateOnly OperationDate, decimal DeltaAmount);
