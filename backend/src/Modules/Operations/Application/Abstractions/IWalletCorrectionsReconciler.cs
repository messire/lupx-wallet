using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Recalculates the balance corrections of a wallet inside a command, before its changes are
/// saved (ADR-0003, §6: the amounts are recalculated in the same transaction as the change).
/// </summary>
public interface IWalletCorrectionsReconciler
{
    /// <summary>
    /// Recalculates every correction of the wallet from its operation journal and returns the
    /// total change of the correction amounts, which the caller adds to the wallet balance delta.
    /// <paramref name="pendingOperations"/> are the operations the command created or changed and
    /// has not saved yet (in their current in-memory state); their persisted versions are ignored.
    /// Returns zero without reading the journal when the wallet has no corrections.
    /// </summary>
    Task<decimal> ReconcileAsync(
        WalletId walletId, IReadOnlyCollection<Operation> pendingOperations, CancellationToken cancellationToken);
}
