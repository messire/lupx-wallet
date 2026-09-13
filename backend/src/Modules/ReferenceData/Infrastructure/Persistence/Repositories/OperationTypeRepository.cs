using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class OperationTypeRepository(ReferenceDataDbContext dbContext) : IOperationTypeRepository
{
    public void Add(OperationType operationType) => dbContext.OperationTypes.Add(operationType);

    public void Remove(OperationType operationType) => dbContext.OperationTypes.Remove(operationType);

    public Task<OperationType?> GetByIdAsync(OperationTypeId id, CancellationToken cancellationToken) =>
        dbContext.OperationTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<OperationTypePageResult> ListAsync(
        bool includeInactive,
        OperationBehaviorKindId? behaviorKindId,
        NameCursor? afterCursor,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = dbContext.OperationTypes.AsQueryable();

        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (behaviorKindId is { } kindId)
        {
            query = query.Where(x => x.BehaviorKindId == kindId);
        }

        if (afterCursor is not null)
        {
            var cursorName = afterCursor.Name;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(x =>
                string.Compare(x.Name, cursorName) > 0 ||
                (x.Name == cursorName && x.CreatedAt > cursorCreatedAt));
        }

        var items = await query
            .OrderBy(x => x.Name)
            .ThenBy(x => x.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new OperationTypePageResult(items, hasMore);
    }
}
