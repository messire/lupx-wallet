using LupexWallet.SharedKernel;

namespace LupexWallet.Audit.Domain;

/// <summary>
/// Агрегат AuditEntry (ddd-model.md, §2.8) — одна неизменяемая запись журнала аудита.
/// Append-only: создаётся один раз через Create, не имеет методов изменения, никогда не
/// поднимает собственных доменных событий (никто не подписывается на "AuditEntry создан") —
/// в отличие от прочих агрегатов проекта, наследует AggregateRoot&lt;TId&gt; только ради
/// единообразия Equals/GetHashCode по Id, не ради событий.
///
/// Не ссылается на другие агрегаты напрямую типами других контекстов — только по паре
/// (EntityType: string, EntityId: Guid), чтобы Audit оставался независимым модулем без
/// обратных зависимостей от Wallets/ReferenceData/Operations/BalanceHistory/ExchangeRates.
/// </summary>
public sealed class AuditEntry : AggregateRoot<AuditEntryId>
{
    public string EntityType { get; private set; } = null!;

    public Guid EntityId { get; private set; }

    public string Action { get; private set; } = null!;

    public DateTimeOffset OccurredAt { get; private set; }

    public AuditActor Actor { get; private set; } = null!;

    public IReadOnlyList<AuditFieldChange> Changes { get; private set; } = [];

    private AuditEntry()
    {
        // Только для EF Core.
    }

    public static AuditEntry Create(
        string entityType, Guid entityId, string action, AuditActor actor, IReadOnlyList<AuditFieldChange>? changes = null)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new AuditEntityTypeRequiredException();
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new AuditActionRequiredException();
        }

        return new AuditEntry
        {
            Id = AuditEntryId.New(),
            EntityType = entityType,
            EntityId = entityId,
            Action = action,
            OccurredAt = MonotonicClock.UtcNow(),
            Actor = actor,
            Changes = changes ?? [],
        };
    }
}
