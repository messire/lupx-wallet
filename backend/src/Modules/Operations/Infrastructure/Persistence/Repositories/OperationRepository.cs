using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationRepository(OperationsDbContext dbContext) : IOperationRepository
{
    public void Add(Operation operation) => dbContext.Operations.Add(operation);

    public void Remove(Operation operation) => dbContext.Operations.Remove(operation);

    public Task<Operation?> GetByIdAsync(OperationId id, CancellationToken cancellationToken) =>
        dbContext.Operations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<OperationPageResult> ListAsync(
        OperationListFilter filter, OperationCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Operations.AsQueryable();

        if (filter.WalletId is { } walletId)
        {
            query = query.Where(x => x.WalletId == walletId);
        }

        if (filter.OperationTypeId is { } operationTypeId)
        {
            query = query.Where(x => x.OperationTypeId == operationTypeId);
        }

        if (filter.DateFrom is { } dateFrom)
        {
            query = query.Where(x => x.OperationDate >= dateFrom);
        }

        if (filter.DateTo is { } dateTo)
        {
            query = query.Where(x => x.OperationDate <= dateTo);
        }

        if (afterCursor is not null)
        {
            var cursorDate = afterCursor.OperationDate;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(x =>
                x.OperationDate > cursorDate ||
                (x.OperationDate == cursorDate && x.CreatedAt > cursorCreatedAt));
        }

        var items = await query
            .OrderBy(x => x.OperationDate)
            .ThenBy(x => x.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new OperationPageResult(items, hasMore);
    }
}
