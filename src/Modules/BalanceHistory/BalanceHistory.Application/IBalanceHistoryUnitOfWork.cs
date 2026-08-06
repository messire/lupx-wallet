using LupexWallet.SharedKernel;

namespace LupexWallet.BalanceHistory.Application;

public interface IBalanceHistoryUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Сериализует конкурентные пересчеты одного и того же кошелька (Postgres advisory
    /// transaction lock — снимается автоматически при завершении транзакции). Без этого
    /// два конкурентных RecalculateFromAsync для одного кошелька (например, ретроактивное
    /// изменение операции + одновременный плановый прогон в 12:00 UTC) оба могут увидеть
    /// "слепка на дату D нет" и попытаться его вставить — второй INSERT упадет с нарушением
    /// первичного ключа (wallet_id, snapshot_date) и откатит всю команду целиком, включая
    /// исходную операцию, которая сама по себе была корректна.
    /// </summary>
    Task AcquireWalletLockAsync(WalletId walletId, CancellationToken cancellationToken);
}
