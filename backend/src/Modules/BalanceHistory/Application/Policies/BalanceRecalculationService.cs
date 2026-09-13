using LupexWallet.BalanceHistory.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// The single balance snapshot recalculation mechanism (ddd-model.md, §2.6:
/// "BalanceRecalculationService"), shared by three triggers that must all use the same
/// balance-on-date algorithm (ADR-0003, ADR-0004):
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
            // Кошелек не может быть физически удален, если у него есть операции/слепки
            // (ddd-model.md §5), поэтому это не должно происходить в норме — защитная
            // проверка на случай гонки с другим процессом, не бросаем исключение, т.к.
            // вызывающий код (обработчик события/фоновая задача) не должен падать из-за
            // не относящейся к нему проблемы.
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
