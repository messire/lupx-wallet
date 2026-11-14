using LupexWallet.Planning.Application;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Planning.Infrastructure;

public sealed class PlannedTransactionRepository(PlanningDbContext dbContext) : IPlannedTransactionRepository
{
    private IQueryable<PlannedTransaction> WithChildren =>
        dbContext.PlannedTransactions.Include(x => x.Overrides).Include(x => x.Executions).AsSplitQuery();

    public void Add(PlannedTransaction plan) => dbContext.PlannedTransactions.Add(plan);

    public void Remove(PlannedTransaction plan) => dbContext.PlannedTransactions.Remove(plan);

    public Task<PlannedTransaction?> GetByIdAsync(PlannedTransactionId id, CancellationToken cancellationToken) =>
        WithChildren.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<PlannedTransaction?> GetForUpdateAsync(PlannedTransactionId id, CancellationToken cancellationToken)
    {
        // Row lock held until the command's transaction ends (TransactionBehavior).
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM planning.planned_transactions WHERE id = {id.Value} FOR UPDATE", cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<PlannedTransactionPageResult> ListAsync(
        PlannedTransactionStatus? status, PlannedTransactionCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = WithChildren;
        if (status is { } value)
        {
            query = query.Where(x => x.Status == value);
        }

        if (afterCursor is not null)
        {
            // CreatedAt has microsecond precision; like the other lists, ties are not expected for a single user.
            var createdAt = afterCursor.CreatedAt;
            query = query.Where(x => x.CreatedAt > createdAt);
        }

        var items = await query
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        return new PlannedTransactionPageResult(hasMore ? items.Take(limit).ToList() : items, hasMore);
    }

    public async Task<IReadOnlyList<PlannedTransaction>> ListAllAsync(PlannedTransactionId? onlyId, CancellationToken cancellationToken)
    {
        var query = WithChildren.AsNoTracking();
        if (onlyId is { } id)
        {
            query = query.Where(x => x.Id == id);
        }

        return await query.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<PlannedTransaction>> ListByWalletAsync(WalletId walletId, CancellationToken cancellationToken) =>
        await WithChildren.Where(x => x.WalletId == walletId).ToListAsync(cancellationToken);
}

public sealed class PlanningUnitOfWork(PlanningDbContext dbContext) : IPlanningUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
