using LupexWallet.SharedKernel;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// Обратный read-контракт (ADR-0009, случай a): «есть ли у кошелька операции/слепки
/// баланса» — нужен DeleteWalletCommand (Q2) и ChangeWalletCurrencyCommand (Q15) для
/// вычисления hasHistory. Порт объявлен здесь, в Application модуля-потребителя
/// (Wallets), реализации — в Infrastructure модулей-владельцев данных: ожидается ровно
/// две регистрации — Operations.Infrastructure.OperationsWalletHistorySource
/// (AddOperationsModule) и BalanceHistory.Infrastructure.BalanceHistoryWalletHistorySource
/// (AddBalanceHistoryModule). Потребитель внедряет IEnumerable&lt;IWalletHistorySource&gt; и
/// считает hasHistory = true, если хотя бы один источник подтвердил историю; пустой
/// список реализаций — ошибка конфигурации DI (fail-fast), а не «истории нет» (см.
/// ADR-0009, «Обязательное условие: пустой список реализаций — ошибка, не «false»»).
/// </summary>
public interface IWalletHistorySource
{
    Task<bool> HasHistoryAsync(WalletId walletId, CancellationToken cancellationToken);
}

/// <summary>
/// Общая точка агрегации для потребителей порта (DeleteWalletCommand,
/// ChangeWalletCurrencyCommand) — вынесена сюда, чтобы не дублировать проверку "пустой
/// список реализаций — ошибка конфигурации DI" (ADR-0009) в каждом обработчике.
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
