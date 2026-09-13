namespace LupexWallet.ReferenceData.Application;

/// <summary>
/// Inverted read contract (ADR-0009, case c): "is this reference item used by at least one
/// record" — used by Delete{WalletType|OperationType|Currency}CommandHandler (decided, Q11;
/// ADR-0002) to compute isUsed. Port declared here, in the consumer's (ReferenceData)
/// Application layer; implementations live in the data-owning modules' Infrastructure: at
/// least Wallets.Infrastructure.WalletsReferenceItemUsageProbe (WalletType/Currency,
/// AddWalletsModule) and Operations.Infrastructure.OperationsReferenceItemUsageProbe
/// (OperationType/Currency, AddOperationsModule) are expected;
/// ExchangeRates.Infrastructure.ExchangeRatesReferenceItemUsageProbe (Currency) — see
/// docs/PROGRESS.md, W1.1/"Known simplification". The consumer injects
/// IEnumerable&lt;IReferenceItemUsageProbe&gt; and treats isUsed as true if any source
/// confirms it; an empty implementation list is a DI configuration error (fail-fast), not
/// "not used" (ADR-0009, "Mandatory condition"). An implementation for which a given kind
/// is not relevant returns false for it (it is still registered, it just holds no
/// references of that item kind).
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
/// AnyHasHistoryAsync (ADR-0009), to avoid duplicating the "empty implementation list is a
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
