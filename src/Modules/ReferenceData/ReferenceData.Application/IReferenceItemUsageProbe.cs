namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Обратный read-контракт (ADR-0009, случай c): «используется ли элемент справочника
/// хотя бы одной записью» — нужен Delete{WalletType|OperationType|Currency}CommandHandler
/// (решено, Q11; ADR-0002) для вычисления isUsed. Порт объявлен здесь, в Application
/// модуля-потребителя (ReferenceData), реализации — в Infrastructure модулей-владельцев
/// данных: ожидаются как минимум Wallets.Infrastructure.WalletsReferenceItemUsageProbe
/// (WalletType/Currency, AddWalletsModule) и Operations.Infrastructure.
/// OperationsReferenceItemUsageProbe (OperationType/Currency, AddOperationsModule);
/// ExchangeRates.Infrastructure.ExchangeRatesReferenceItemUsageProbe (Currency) — см.
/// docs/PROGRESS.md, W1.1/"Известное упрощение". Потребитель внедряет
/// IEnumerable&lt;IReferenceItemUsageProbe&gt; и считает isUsed = true, если хотя бы один
/// источник подтвердил использование; пустой список реализаций — ошибка конфигурации DI
/// (fail-fast), а не «не используется» (ADR-0009, «Обязательное условие»). Реализация,
/// которой конкретный kind не релевантен, возвращает false для него (не участвует в
/// пустом списке — она зарегистрирована, просто не хранит ссылок на этот вид элемента).
/// </summary>
public interface IReferenceItemUsageProbe
{
    Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken);
}

public enum ReferenceItemKind
{
    WalletType,
    OperationType,
    Currency,
}

/// <summary>
/// Общая точка агрегации для потребителей порта (Delete{WalletType|OperationType|Currency}
/// CommandHandler) — вынесена сюда по аналогии с
/// Wallets.Application.WalletHistorySourceExtensions.AnyHasHistoryAsync (ADR-0009), чтобы не
/// дублировать проверку "пустой список реализаций — ошибка конфигурации DI" в каждом
/// обработчике.
/// </summary>
public static class ReferenceItemUsageProbeExtensions
{
    public static async Task<bool> AnyIsUsedAsync(
        this IEnumerable<IReferenceItemUsageProbe> probes,
        ReferenceItemKind kind,
        Guid referenceItemId,
        CancellationToken cancellationToken)
    {
        var materializedProbes = probes as ICollection<IReferenceItemUsageProbe> ?? probes.ToList();
        if (materializedProbes.Count == 0)
        {
            throw new InvalidOperationException(
                "Не зарегистрировано ни одной реализации IReferenceItemUsageProbe — ожидались как " +
                "минимум Wallets.Infrastructure.WalletsReferenceItemUsageProbe (AddWalletsModule) и " +
                "Operations.Infrastructure.OperationsReferenceItemUsageProbe (AddOperationsModule). " +
                "Пустой список — ошибка конфигурации DI, не «не используется» (ADR-0009, «Обязательное " +
                "условие: пустой список реализаций — ошибка, не «false»»).");
        }

        // Последовательно, не Task.WhenAll: несколько проб внутри одной ambient-транзакции
        // (System.Transactions.TransactionScope, TransactionBehavior) конкурировали бы за один
        // и тот же физический connection — Npgsql не поддерживает параллельные команды на одном
        // соединении в рамках TransactionScope. Короткое замыкание на первом true — тот же
        // результат, что и Task.WhenAll + Any, без риска.
        foreach (var probe in materializedProbes)
        {
            if (await probe.IsUsedAsync(kind, referenceItemId, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
