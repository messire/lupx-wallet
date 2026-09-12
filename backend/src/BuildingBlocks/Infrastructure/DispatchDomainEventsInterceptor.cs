using System.Globalization;
using System.Runtime.CompilerServices;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// EF Core SaveChanges-перехватчик: собирает доменные события со всех отслеживаемых
/// агрегатов (IHasDomainEvents) и публикует их через IDomainEventDispatcher. Подключается
/// в OnConfiguring/AddInterceptors каждого DbContext модуля — единая точка диспетчеризации,
/// не дублируется в каждом модуле (high-level-architecture.md, §4).
///
/// Сбор происходит в SavingChangesAsync (ДО физического сохранения) — для удаляемых
/// агрегатов запись становится Detached сразу после успешного сохранения, и её события
/// были бы потеряны, если собирать их постфактум. Диспетчеризация (вызов подписчиков)
/// происходит в SavedChangesAsync (ПОСЛЕ физического сохранения, ADR-0008) — это
/// обязательно для подписчиков, которые сами читают данные, только что записанные текущим
/// SaveChangesAsync, через собственный DbContext того же DI-scope (например, BalanceHistory
/// читает Operations через IWalletOperationsLookup при пересчете истории баланса, ADR-0003):
/// если бы диспетчеризация происходила до отправки SQL в БД (как раньше), запрос
/// подписчика их бы еще не увидел, даже через тот же экземпляр DbContext, — SaveChanges
/// на момент SavingChangesAsync еще не отправил команды в БД.
///
/// Диспетчеризация все равно происходит внутри той же System.Transactions.TransactionScope,
/// что и сам SaveChanges (см. TransactionBehavior), поэтому откат физического сохранения
/// (или сохранения подписчика) по-прежнему откатывает весь набор побочных эффектов.
///
/// Экземпляр интерцептора — общий на весь DI-scope (каждый модуль регистрирует
/// AddScoped&lt;DispatchDomainEventsInterceptor&gt;, но в рамках одного HTTP-запроса
/// GetRequiredService возвращает один и тот же экземпляр для всех DbContext'ов модулей).
/// Буфер событий поэтому ключуется по конкретному экземпляру DbContext
/// (ConditionalWeakTable), а не хранится в обычном поле интерцептора — иначе вложенный
/// SaveChanges на другом DbContext (ровно то, что делает BalanceHistory-подписчик) затер
/// бы буфер, пока внешний SaveChanges еще не дошел до диспетчеризации.
///
/// ADR-0010: помимо сбора событий, CollectEvents теперь же (SavingChangesAsync, пока
/// OriginalValues/CurrentValues еще различаются) вычисляет diff old/new по каждому
/// изменившемуся полю агрегата и кладет его в отдельный словарь по EventId — подписчик
/// Audit.Infrastructure забирает его в момент диспетчеризации через TakeChanges(eventId),
/// не читая ChangeTracker напрямую (на момент SavedChangesAsync состояние уже "принято").
/// </summary>
public sealed class DispatchDomainEventsInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    private readonly ConditionalWeakTable<DbContext, List<IDomainEvent>> _pendingEventsByContext = new();

    /// <summary>
    /// Diff old/new по каждому событию (ADR-0010), ключ — IDomainEvent.EventId. Не привязан
    /// к конкретному DbContext (в отличие от _pendingEventsByContext) — подписчики
    /// (Audit.Infrastructure) читают его в SavedChangesAsync того же DbContext, что вычислил
    /// diff в предшествующем SavingChangesAsync, но сам интерцептор общий на весь DI-scope
    /// (см. класс-комментарий), поэтому простого Dictionary достаточно: в пределах одной
    /// команды SaveChanges разных DbContext выполняются последовательно, не параллельно.
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
    /// Если SaveChangesAsync упал, буфер для этого DbContext больше не нужен — иначе он
    /// "прилипнет" к следующему, не связанному с ним успешному SaveChangesAsync на том же
    /// экземпляре DbContext (сценарий из реальной практики: IWalletBalanceGateway.
    /// ApplyDeltaAsync вызывает SaveChangesAsync несколько раз за одну команду, например
    /// при переводе — по разу на кошелек, оба раза на одном экземпляре WalletsDbContext).
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
    /// Синхронный SaveChanges не поддерживается — диспетчеризация событий реализована
    /// только для async-пути (ADR-0008). Защитная проверка: если бы синхронный путь
    /// использовался, события собирались бы (агрегаты очищались бы от них), но никогда не
    /// диспетчеризовались — тихая потеря истории баланса вместо явной ошибки при разработке.
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
    /// Забирает (и удаляет) diff, вычисленный для конкретного события — вызывается
    /// подписчиками Audit.Infrastructure (ADR-0010) в момент диспетчеризации
    /// (SavedChangesAsync). Удаление сразу после чтения не даёт словарю расти сверх
    /// количества ещё не обработанных в рамках текущей команды событий.
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
    /// Diff по собственным (не навигационным) свойствам сущности — ключевые свойства
    /// исключены (они идентичность записи, а не "изменившееся значение"; см. ADR-0010,
    /// раздел про BalanceSnapshot, где это исключение оказалось значимым и потребовало
    /// точечного обогащения событий на стороне Audit).
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
