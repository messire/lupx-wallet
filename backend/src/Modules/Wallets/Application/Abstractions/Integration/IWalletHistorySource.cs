using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Inverted read contract (ADR-0009, case a): "does this wallet have operations/balance
/// snapshots" — used by DeleteWalletCommand (Q2) and ChangeWalletCurrencyCommand (Q15) to
/// compute hasHistory. Port declared here, in the consumer's (Wallets) Application layer;
/// implementations live in the data-owning modules' Infrastructure: exactly two
/// registrations are expected — Operations.Infrastructure.OperationsWalletHistorySource
/// (AddOperationsModule) and BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource
/// (AddBalanceHistoryModule). The consumer injects IEnumerable&lt;IWalletHistorySource&gt;
/// and treats hasHistory as true if any source confirms it; an empty implementation list is
/// a DI configuration error (fail-fast), not "no history" (ADR-0009).
/// </summary>
public interface IWalletHistorySource
{
    Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken);
}

/// <summary>
/// Shared aggregation point for port consumers (DeleteWalletCommand,
/// ChangeWalletCurrencyCommand) — avoids duplicating the "empty implementation list is a DI
/// configuration error" check (ADR-0009) in every handler.
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
                "Не зарегистрировано ни одной реализации IWalletHistorySource — ожидались " +
                "Operations.Infrastructure.OperationsWalletHistorySource (AddOperationsModule) и " +
                "BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource " +
                "(AddBalanceHistoryModule). Пустой список — ошибка конфигурации DI, не «истории нет» " +
                "(ADR-0009, «Обязательное условие: пустой список реализаций — ошибка, не «false»»).");
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
