namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Consumer-owned port (ADR-0001 §2, way d; usage rule — ADR-0002 §4): "is this reference
/// item used by at least one record". Used by Delete{WalletType|OperationType|Currency}
/// CommandHandler. Exactly two implementations are expected, one per data owner:
/// Wallets.Infrastructure.WalletsReferenceItemUsageProbe (WalletType/Currency, archived
/// wallets included) and Operations.Infrastructure.OperationsReferenceItemUsageProbe
/// (OperationType/Currency, soft-deleted operations included). Exchange rates do not count as
/// usage: they are deleted together with the currency (ExchangeRates CurrencyDeletedHandler).
/// The consumer injects IEnumerable&lt;IReferenceItemUsageProbe&gt; and treats isUsed as true if
/// any source confirms it; an empty list is a DI configuration error (fail-fast), not "not
/// used" (ADR-0001 §2, mandatory condition). An implementation for which a kind is not
/// relevant returns false for it.
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
/// Shared aggregation point for port consumers (Delete{WalletType|OperationType|Currency}
/// CommandHandler) — mirrors Wallets.Application.WalletHistorySourceExtensions.
/// AnyHasHistoryAsync (ADR-0001), to avoid duplicating the "empty implementation list is a
/// DI configuration error" check in every handler.
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
                "Не зарегистрировано ни одной реализации IReferenceItemUsageProbe — ожидались ровно две: " +
                "Wallets.Infrastructure.WalletsReferenceItemUsageProbe (AddWalletsModule) и " +
                "Operations.Infrastructure.OperationsReferenceItemUsageProbe (AddOperationsModule; " +
                "удалённые операции считаются, курсы валют — нет). " +
                "Пустой список — ошибка конфигурации DI, не «не используется» (ADR-0001, «Обязательное " +
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
