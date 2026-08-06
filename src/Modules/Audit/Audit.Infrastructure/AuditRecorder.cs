using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Единая точка записи AuditEntry, используемая всеми обработчиками доменных событий в
/// этом проекте (по одному хендлеру на событие каждого модуля-издателя, файлы
/// *AuditHandlers.cs). Живёт в Infrastructure, а не в Application (по аналогии с
/// BalanceHistory.Infrastructure.OperationEventHandlers, ADR-0008): нужен доступ к
/// IAuditActorAccessor и DispatchDomainEventsInterceptor (оба — BuildingBlocks.Infrastructure),
/// которые Application-слой не должен видеть напрямую (high-level-architecture.md, §2 —
/// "Application зависит только от Domain").
/// </summary>
public sealed class AuditRecorder(
    IAuditEntryRepository repository,
    IAuditUnitOfWork unitOfWork,
    IAuditActorAccessor actorAccessor,
    DispatchDomainEventsInterceptor interceptor)
{
    /// <summary>
    /// ADR-0010, механизм по умолчанию: diff берётся из EF Core ChangeTracker того
    /// SaveChanges, который поднял это событие.
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
    /// ADR-0010, точечное обогащение: используется там, где diff из ChangeTracker
    /// бессмыслен (BalanceSnapshot — составной ключ без суррогатного Id теряет SnapshotDate
    /// из diff; ExchangeRates* — батчевые события без единого агрегата-владельца).
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
