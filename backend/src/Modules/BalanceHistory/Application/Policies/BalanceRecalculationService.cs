using LupexWallet.BalanceHistory.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// The single balance snapshot recalculation mechanism (ddd-model.md, §2.6:
/// "BalanceRecalculationService"), shared by three triggers that must all use the same
/// balance-on-date algorithm (ADR-0003):
/// 1. Cascading recalculation on a retroactive operation/transfer change (Operations domain
///    event handlers, see OperationEventHandlers) — fromDate = the operation date (the
///    earlier of old/new on edit).
/// 2. Scheduled daily snapshot creation/update at 12:00 UTC — fromDate = today.
/// 3. Backfilling missed scheduled runs on application startup — fromDate = the latest
///    existing snapshot date + 1 (or the wallet's AccountingStartDate if there are no
///    snapshots yet).
///
/// Recalculates the range [max(fromDate, wallet.AccountingStartDate) .. today] inclusive
/// (decided, Q4 — recalculating a single date only is not allowed). The balance on date D is
/// the wallet's InitialBalance plus the sum of AppliedDelta over all wallet operations with
/// OperationDate ≤ D — this value already accounts for the operation kind/behavior (Income/
/// Expense/Adjustment/Transfer), computed once in Operations.Application
/// (OperationEffectCalculator) when the operation is created/changed, so it's enough here to
/// sum the ready-made deltas (IWalletOperationsLookup) without repeating that logic.
///
/// Balance corrections (ADR-0003, §6) are ordinary operation rows with their own applied
/// delta, so the balance on a date includes the correction amounts dated on or before it. On the
/// date of a stated balance the snapshot therefore equals the stated amount X (the correction
/// is computed as X minus the balance without it).
/// </summary>
public sealed class BalanceRecalculationService(
    IBalanceSnapshotRepository repository,
    IBalanceHistoryUnitOfWork unitOfWork,
    IWalletBalanceGateway walletGateway,
    IWalletOperationsLookup operationsLookup)
{
    public async Task RecalculateFromAsync(WalletId walletId, DateOnly fromDate, CancellationToken cancellationToken)
    {
        // Сериализует конкурентные пересчеты этого кошелька (см. IBalanceHistoryUnitOfWork.
        // AcquireWalletLockAsync) — до любого чтения/записи, чтобы второй конкурентный
        // вызов не увидел промежуточное состояние первого.
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var wallet = await walletGateway.GetAsync(walletId, cancellationToken);
        if (wallet is null)
        {
            // The wallet is already gone (deleted in the same command or by a concurrent one;
            // its snapshots are removed by WalletDeletedHandler, ADR-0002, §2) — nothing to
            // recalculate. Not an error: the caller (event handler/background job) must not fail.
            return;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var effectiveFrom = fromDate < wallet.AccountingStartDate ? wallet.AccountingStartDate : fromDate;
        if (effectiveFrom > today)
        {
            return;
        }

        var deltas = await operationsLookup.GetDailyDeltasUpToAsync(walletId, today, cancellationToken);
        var deltasBeforeFrom = deltas.Where(d => d.OperationDate < effectiveFrom).Sum(d => d.DeltaAmount);
        var deltasFromByDate = deltas
            .Where(d => d.OperationDate >= effectiveFrom)
            .ToDictionary(d => d.OperationDate, d => d.DeltaAmount);

        var existingSnapshots = (await repository.GetRangeAsync(walletId, effectiveFrom, today, cancellationToken))
            .ToDictionary(s => s.SnapshotDate);

        var runningBalance = wallet.InitialBalance.Amount + deltasBeforeFrom;

        for (var date = effectiveFrom; date <= today; date = date.AddDays(1))
        {
            if (deltasFromByDate.TryGetValue(date, out var dayDelta))
            {
                runningBalance += dayDelta;
            }

            var balance = new Money(runningBalance, wallet.CurrencyId);

            if (existingSnapshots.TryGetValue(date, out var existing))
            {
                if (existing.Balance.Amount != balance.Amount)
                {
                    existing.UpdateBalance(balance);
                }
            }
            else
            {
                repository.Add(BalanceSnapshot.Create(walletId, date, balance));
            }
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
