using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Audit.Infrastructure;

public sealed class AuditEntryRepository(AuditDbContext dbContext) : IAuditEntryRepository
{
    public void Add(AuditEntry entry) => dbContext.AuditEntries.Add(entry);

    public async Task<AuditEntryPageResult> ListAsync(
        string entityType, Guid entityId, AuditEntryCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var baseQuery = dbContext.AuditEntries
            .Where(x => x.EntityType == entityType && x.EntityId == entityId);

        List<AuditEntry> items;

        if (afterCursor is not null)
        {
            var cursorOccurredAt = afterCursor.OccurredAt;
            var cursorId = afterCursor.Id;

            // Guid (AuditEntryId.Value) не транслируется EF Core/Npgsql в SQL-сравнение
            // "меньше" — у CLR Guid нет операторов сравнения (`<`/`>`), только CompareTo,
            // который в WHERE-предикате не переводится ("could not be translated"). Тай-брейкер
            // по Id поэтому применяется в памяти, но только к узкому набору строк с ТОЧНО тем
            // же occurred_at, что и курсор (коллизия MonotonicClock/timestamptz, M1) — такой
            // набор всегда мал (совпадение меток на одной сущности), полная материализация
            // истории сущности не требуется.
            var strictlyBefore = await baseQuery
                .Where(x => x.OccurredAt < cursorOccurredAt)
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);

            var tiedAtCursor = (await baseQuery
                    .Where(x => x.OccurredAt == cursorOccurredAt)
                    .ToListAsync(cancellationToken))
                .Where(x => x.Id.Value.CompareTo(cursorId) < 0);

            items = tiedAtCursor
                .Concat(strictlyBefore)
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id.Value)
                .Take(limit + 1)
                .ToList();
        }
        else
        {
            items = await baseQuery
                .OrderByDescending(x => x.OccurredAt)
                .ThenByDescending(x => x.Id)
                .Take(limit + 1)
                .ToListAsync(cancellationToken);
        }

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new AuditEntryPageResult(items, hasMore);
    }
}
