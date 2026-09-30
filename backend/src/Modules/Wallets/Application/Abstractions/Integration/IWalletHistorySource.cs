using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Consumer-owned port (ADR-0001, §2, way d): "does this wallet have history" — used by
/// DeleteWalletCommand and ChangeWalletCurrencyCommand. Wallet history is operations only,
/// including soft-deleted ones and both transfer legs; snapshots and the initial balance are
/// not history (ADR-0002, §2). Port declared here, in the consumer's (Wallets) Application
/// layer; exactly one implementation is expected — Operations.Infrastructure.
/// OperationsWalletHistorySource (AddOperationsModule), the data owner. The consumer injects
/// IEnumerable&lt;IWalletHistorySource&gt; and treats hasHistory as true if any source confirms it;
/// an empty implementation list is a DI configuration error (fail-fast), not "no history".
/// </summary>
public interface IWalletHistorySource
{
    Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken);
}

/// <summary>
/// Shared aggregation point for port consumers (DeleteWalletCommand,
/// ChangeWalletCurrencyCommand) — avoids duplicating the "empty implementation list is a DI
/// configuration error" check (ADR-0001) in every handler.
/// </summary>
public static class WalletHistorySourceExtensions
{
    public static async Task<bool> AnyHasHistoryAsync(
        this IEnumerable<IWalletHistorySource> sources, WalletId walletId, CancellationToken cancellationToken)
    {
        var materializedSources = sources as ICollection<IWalletHistorySource> ?? sources.ToList();
        if (materializedSources.Count == 0)
        {
            throw new InvalidOperationException(
                "Не зарегистрировано ни одной реализации IWalletHistorySource — ожидалась " +
                "Operations.Infrastructure.OperationsWalletHistorySource (AddOperationsModule, " +
                "единственный владелец данных истории, ADR-0002 §2). Пустой список — ошибка " +
                "конфигурации DI, не «истории нет» (ADR-0001, «Обязательное условие: пустой " +
                "список реализаций — ошибка, не «false»»).");
        }

        // Последовательно, не Task.WhenAll: несколько источников внутри одной ambient-
        // транзакции (System.Transactions.TransactionScope, TransactionBehavior) конкурировали
        // бы за один и тот же физический connection — Npgsql не поддерживает параллельные
        // команды на одном соединении в рамках TransactionScope. Короткое замыкание на первом
        // true — тот же результат, что и Task.WhenAll + Any, без риска.
        foreach (var source in materializedSources)
        {
            if (await source.HasHistoryAsync(walletId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
