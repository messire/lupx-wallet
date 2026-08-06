using LupexWallet.BalanceHistory.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;

namespace LupexWallet.BalanceHistory.Application;

/// <summary>
/// Единый механизм пересчета слепков баланса (ddd-model.md, §2.6: "BalanceRecalculationService"),
/// общий для трех триггеров, обязанных использовать один и тот же алгоритм расчета
/// баланса на дату (ADR-0003, ADR-0004):
/// 1. Каскадный пересчет при ретроактивном изменении операции/перевода (обработчики
///    доменных событий Operations, см. OperationEventHandlers) — fromDate = дата операции
///    (минимальная из старой/новой при редактировании).
/// 2. Плановое ежедневное создание/обновление слепка в 12:00 UTC — fromDate = сегодня.
/// 3. Досоздание пропущенных плановых запусков при старте приложения — fromDate = дата
///    последнего существующего слепка + 1 (или AccountingStartDate кошелька, если слепков
///    еще нет).
///
/// Пересчитывает диапазон [max(fromDate, wallet.AccountingStartDate) .. сегодня] включительно
/// (решено, Q4 — частичный пересчет только одной даты не допускается). Баланс на дату D
/// определяется как InitialBalance кошелька плюс сумма AppliedDelta всех операций кошелька
/// с OperationDate ≤ D — это значение уже учитывает знак/поведение операции (Income/
/// Expense/Adjustment/Transfer), вычисленный один раз в Operations.Application
/// (OperationEffectCalculator) при создании/изменении операции, поэтому здесь достаточно
/// просуммировать готовые дельты (IWalletOperationsLookup), не повторяя эту логику.
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
