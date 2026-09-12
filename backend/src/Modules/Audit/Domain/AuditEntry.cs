using LupexWallet.SharedKernel;

namespace LupexWallet.Audit.Domain;

/// <summary>
/// AuditEntry aggregate (ddd-model.md, §2.8) — a single immutable audit log record.
/// Append-only: created once via Create, has no mutation methods, never raises its own domain
/// events (nothing subscribes to "AuditEntry created") — unlike other aggregates in the project,
/// it inherits AggregateRoot&lt;TId&gt; only for uniform Equals/GetHashCode by Id, not for events.
///
/// Does not reference other aggregates by type from other bounded contexts — only by the pair
/// (EntityType: string, EntityId: Guid), so Audit stays an independent module with no reverse
/// dependencies on Wallets/ReferenceData/Operations/BalanceHistory/ExchangeRates.
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
