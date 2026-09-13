using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Single write point for AuditEntry, used by all domain event handlers in this project (one
/// handler per event per publisher module, the *AuditHandlers.cs files). Lives in
/// Infrastructure, not Application (by analogy with
/// BalanceHistory.Infrastructure.OperationEventHandlers, ADR-0008): it needs access to
/// IAuditActorAccessor and DispatchDomainEventsInterceptor (both BuildingBlocks.Infrastructure),
/// which the Application layer must not see directly (high-level-architecture.md, §2 —
/// "Application depends only on Domain").
/// </summary>
public sealed class AuditRecorder(
    IAuditEntryRepository repository,
    IAuditUnitOfWork unitOfWork,
    IAuditActorAccessor actorAccessor,
    DispatchDomainEventsInterceptor interceptor)
{
    /// <summary>
    /// ADR-0010, default mechanism: the diff is taken from the EF Core ChangeTracker of the
    /// SaveChanges call that raised this event.
    /// </summary>
    public Task RecordFromChangeTrackerAsync(
        IDomainEvent domainEvent, string entityType, Guid entityId, string action, CancellationToken cancellationToken)
    {
        var changes = interceptor.TakeChanges(domainEvent.EventId)
            .Select(c => new AuditFieldChange(c.FieldName, c.OldValue, c.NewValue))
            .ToList();

        return RecordAsync(entityType, entityId, action, changes, cancellationToken);
    }

    /// <summary>
    /// ADR-0010, targeted enrichment: used where a ChangeTracker diff is meaningless
    /// (BalanceSnapshot — a composite key without a surrogate Id drops SnapshotDate from the
    /// diff; ExchangeRates* — batched events with no single owning aggregate).
    /// </summary>
    public async Task RecordAsync(
        string entityType, Guid entityId, string action, IReadOnlyList<AuditFieldChange> changes, CancellationToken cancellationToken)
    {
        var actorContext = actorAccessor.Current;
        var actor = actorContext.IsSystem
            ? AuditActor.System(actorContext.SystemProcessName ?? "Unknown")
            : AuditActor.User();

        var entry = AuditEntry.Create(entityType, entityId, action, actor, changes);

        repository.Add(entry);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
