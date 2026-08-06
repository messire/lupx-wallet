using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// EF Core SaveChanges-перехватчик: перед физическим сохранением собирает доменные
/// события со всех отслеживаемых агрегатов (IHasDomainEvents) и публикует их через
/// IDomainEventDispatcher. Подключается в OnConfiguring/AddInterceptors каждого
/// DbContext модуля — единая точка диспетчеризации, не дублируется в каждом модуле
/// (high-level-architecture.md, §4).
///
/// Важно: события собираются и диспетчеризуются в SavingChangesAsync (ДО SaveChanges),
/// а не в SavedChangesAsync (после) — для удаляемых агрегатов запись становится
/// Detached сразу после успешного сохранения, и её события были бы потеряны, если
/// собирать их постфактум. Диспетчеризация все равно происходит внутри той же
/// System.Transactions.TransactionScope, что и сам SaveChanges (см. TransactionBehavior),
/// поэтому откат физического сохранения по-прежнему откатывает и побочные эффекты
/// подписчиков.
/// </summary>
public sealed class DispatchDomainEventsInterceptor(IDomainEventDispatcher dispatcher) : SaveChangesInterceptor
{
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await DispatchEventsAsync(eventData.Context, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private async Task DispatchEventsAsync(DbContext context, CancellationToken cancellationToken)
    {
        var aggregatesWithEvents = context.ChangeTracker
            .Entries<IHasDomainEvents>()
            .Where(entry => entry.Entity.DomainEvents.Count > 0)
            .Select(entry => entry.Entity)
            .ToList();

        var domainEvents = aggregatesWithEvents
            .SelectMany(aggregate => aggregate.DomainEvents)
            .ToList();

        foreach (var aggregate in aggregatesWithEvents)
        {
            aggregate.ClearDomainEvents();
        }

        if (domainEvents.Count > 0)
        {
            await dispatcher.DispatchAsync(domainEvents, cancellationToken);
        }
    }
}
