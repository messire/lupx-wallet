using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.Operations.Application;

/// <summary>
/// Merges the persisted journal of a wallet with the operations of the running command and
/// recalculates all its corrections. Domain events are published only after SaveChanges, so the
/// journal cannot be read back after an intermediate save: the unsaved operations are taken from
/// memory and their persisted rows are excluded from the database read (otherwise an edited
/// operation would be counted twice).
/// </summary>
public sealed class WalletCorrectionsReconciler(
    IOperationRepository repository,
    IWalletBalanceGateway walletBalanceGateway) : IWalletCorrectionsReconciler
{
    public async Task<decimal> ReconcileAsync(
        WalletId walletId, IReadOnlyCollection<Operation> pendingOperations, CancellationToken cancellationToken)
    {
        var pendingCorrections = pendingOperations.Where(x => x.IsCorrection && x.WalletId == walletId).ToList();

        if (pendingCorrections.Count == 0 && !await repository.AnyCorrectionsAsync(walletId, cancellationToken))
        {
            return 0m;
        }

        var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken)
            ?? throw new WalletReferenceNotFoundException(walletId.Value);

        var corrections = (await repository.GetCorrectionsAsync(walletId, cancellationToken)).ToList();
        foreach (var pending in pendingCorrections.Where(p => corrections.All(c => c.Id != p.Id)))
        {
            corrections.Add(pending);
        }

        var changedIds = pendingOperations.Where(x => !x.IsCorrection).Select(x => x.Id).ToList();

        // Operations after the last stated balance cannot affect any correction, so they are not read.
        DateOnly? lastStatedDate = corrections.Count == 0 ? null : corrections.Max(c => c.OperationDate);
        var ledger = (await repository.GetRegularLedgerAsync(walletId, changedIds, lastStatedDate, cancellationToken)).ToList();
        ledger.AddRange(pendingOperations
            .Where(x => !x.IsCorrection && x.WalletId == walletId && !x.IsDeleted)
            .Select(x => new WalletLedgerEntry(x.OperationDate, x.AppliedDelta.Amount)));

        var amounts = StatedBalanceCorrectionCalculator.Calculate(
            wallet.InitialBalance.Amount,
            ledger,
            corrections.Select(c => new CorrectionInput(c.Id, c.OperationDate, c.StatedBalance!.Value.Amount)));

        var totalChange = 0m;
        foreach (var amount in amounts)
        {
            var correction = corrections.Single(c => c.Id == amount.Id);
            var previous = correction.Amount.Amount;
            correction.ApplyRecalculatedDelta(amount.Amount);
            totalChange += amount.Amount - previous;
        }

        return totalChange;
    }
}
