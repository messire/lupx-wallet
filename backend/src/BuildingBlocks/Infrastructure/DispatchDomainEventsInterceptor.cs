using System.Globalization;
using System.Runtime.CompilerServices;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// EF Core SaveChanges interceptor: collects domain events from all tracked aggregates
/// (IHasDomainEvents) and publishes them via IDomainEventDispatcher. Registered in
/// OnConfiguring/AddInterceptors of each module's DbContext — a single dispatch point,
/// not duplicated per module (high-level-architecture.md, §4).
///
/// Collection happens in SavingChangesAsync (BEFORE the physical save) — for deleted
/// aggregates, the entry becomes Detached right after a successful save, so its events
/// would be lost if collected afterwards. Dispatch (calling subscribers) happens in
/// SavedChangesAsync (AFTER the physical save, ADR-0008) — this is required for
/// subscribers that read data just written by the current SaveChangesAsync through their
/// own DbContext in the same DI scope (e.g. BalanceHistory reads Operations via
/// IWalletOperationsLookup when recalculating balance history, ADR-0003): if dispatch
/// happened before the SQL was sent to the database, a subscriber's query would not see
/// it yet, even through the same DbContext instance — at SavingChangesAsync time,
/// SaveChanges has not sent commands to the database yet.
///
/// Dispatch still runs inside the same System.Transactions.TransactionScope as SaveChanges
/// itself (see TransactionBehavior), so rolling back the physical save (or a subscriber's
/// save) still rolls back the whole set of side effects.
///
/// The interceptor instance is shared across the whole DI scope (each module registers
/// AddScoped&lt;DispatchDomainEventsInterceptor&gt;, but within one HTTP request
/// GetRequiredService returns the same instance for all module DbContexts). The event
/// buffer is therefore keyed by the specific DbContext instance (ConditionalWeakTable)
/// rather than stored in a plain interceptor field — otherwise a nested SaveChanges on
/// another DbContext (exactly what the BalanceHistory subscriber does) would overwrite
/// the buffer while the outer SaveChanges has not reached dispatch yet.
///
/// ADR-0010: besides collecting events, CollectEvents also computes an old/new diff for
/// each changed aggregate field while OriginalValues/CurrentValues still differ
/// (SavingChangesAsync) and stores it in a separate dictionary keyed by EventId — the
/// Audit.Infrastructure subscriber retrieves it at dispatch time via TakeChanges(eventId)
/// instead of reading the ChangeTracker directly (by SavedChangesAsync time, the state is
/// already "accepted").
/// </summary>
public sealed class DispatchDomainEventsInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<IDomainEvent>> _pendingEventsByContext = new();

    /// <summary>
    /// Old/new diff per event (ADR-0010), keyed by IDomainEvent.EventId. Not tied to a
    /// specific DbContext (unlike _pendingEventsByContext) — subscribers
    /// (Audit.Infrastructure) read it in SavedChangesAsync of the same DbContext that
    /// computed the diff in the preceding SavingChangesAsync, but the interceptor itself
    /// is shared across the DI scope (see class summary), so a plain Dictionary is
    /// sufficient: within one command, SaveChanges calls on different DbContexts run
    /// sequentially, not in parallel.
    /// </summary>
    private readonly Dictionary<Guid, IReadOnlyList<EntityFieldChange>> _changesByEventId = new();

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            CollectEvents(eventData.Context);
        }

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null && _pendingEventsByContext.TryGetValue(eventData.Context, out var domainEvents))
        {
            _pendingEventsByContext.Remove(eventData.Context);

            if (domainEvents.Count > 0)
            {
                await dispatcher.DispatchAsync(domainEvents, cancellationToken);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken);
    }

    /// <summary>
    /// If SaveChangesAsync fails, the buffer for this DbContext is no longer needed —
    /// otherwise it would "stick" to the next, unrelated successful SaveChangesAsync on the
    /// same DbContext instance (a real scenario: IWalletBalanceGateway.ApplyDeltaAsync
    /// calls SaveChangesAsync multiple times per command, e.g. for a transfer — once per
    /// wallet, both times on the same WalletsDbContext instance).
    /// </summary>
    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            _pendingEventsByContext.Remove(eventData.Context);
        }

        return base.SaveChangesFailedAsync(eventData, cancellationToken);
    }

    /// <summary>
    /// Synchronous SaveChanges is not supported — event dispatch is implemented only for
    /// the async path (ADR-0008). This is a guard: if the sync path were used, events
    /// would still be collected (aggregates cleared of them) but never dispatched — a
    /// silent loss of balance history instead of a clear error during development.
    /// </summary>
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result) =>
        throw new NotSupportedException(
            "Синхронный DbContext.SaveChanges() не поддерживается в этом проекте — " +
            "диспетчеризация доменных событий (DispatchDomainEventsInterceptor, ADR-0008) " +
            "реализована только для SaveChangesAsync. Используйте SaveChangesAsync.");

    private void CollectEvents(DbContext context)
    {
        var trackedAggregates = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .ToList();

        var domainEvents = new List<IDomainEvent>();
        foreach (var entry in trackedAggregates)
        {
            // Diff вычисляется здесь (SavingChangesAsync, ДО SaveChanges) — после сохранения
            // EntityEntry.OriginalValues сравнялись бы с CurrentValues (ADR-0010).
            var changes = ComputeFieldChanges(entry);

            foreach (var domainEvent in entry.Entity.DomainEvents)
            {
                domainEvents.Add(domainEvent);
                _changesByEventId[domainEvent.EventId] = changes;
            }

            entry.Entity.ClearDomainEvents();
        }

        // AddRange, а не замена буфера: если это повторная попытка SaveChangesAsync на том
        // же экземпляре DbContext после сбоя (например, execution strategy с ретраями),
        // агрегаты из первой попытки уже очищены от событий — повторный сбор дал бы пустой
        // список и затер бы уже накопленные, но еще не отправленные события первой попытки.
        if (_pendingEventsByContext.TryGetValue(context, out var existing))
        {
            existing.AddRange(domainEvents);
        }
        else
        {
            _pendingEventsByContext.Add(context, domainEvents);
        }
    }

    /// <summary>
    /// Retrieves (and removes) the diff computed for a specific event — called by
    /// Audit.Infrastructure subscribers (ADR-0010) at dispatch time (SavedChangesAsync).
    /// Removing it right after reading keeps the dictionary from growing beyond the number
    /// of events not yet processed within the current command.
    /// </summary>
    public IReadOnlyList<EntityFieldChange> TakeChanges(Guid eventId)
    {
        if (_changesByEventId.Remove(eventId, out var changes))
        {
            return changes;
        }

        return [];
    }

    /// <summary>
    /// Diff over the entity's own (non-navigation) properties — key properties are
    /// excluded (they identify the record rather than represent a "changed value"; see
    /// ADR-0010, the BalanceSnapshot section, where this exclusion turned out to matter
    /// and required targeted event enrichment on the Audit side).
    /// </summary>
    private static IReadOnlyList<EntityFieldChange> ComputeFieldChanges(EntityEntry entry)
    {
        var keyPropertyNames = entry.Metadata.FindPrimaryKey()?.Properties
            .Select(p => p.Name)
            .ToHashSet() ?? [];

        var changes = new List<EntityFieldChange>();
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (keyPropertyNames.Contains(name))
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    changes.Add(new EntityFieldChange(name, null, Format(property.CurrentValue)));
                    break;
                case EntityState.Deleted:
                    changes.Add(new EntityFieldChange(name, Format(property.OriginalValue), null));
                    break;
                case EntityState.Modified:
                    if (property.IsModified)
                    {
                        changes.Add(new EntityFieldChange(name, Format(property.OriginalValue), Format(property.CurrentValue)));
                    }

                    break;
            }
        }

        return changes;
    }

    private static string? Format(object? value) => value switch
    {
        null => null,
        DateOnly d => d.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dto => dto.ToString("O", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString(),
    };
}
