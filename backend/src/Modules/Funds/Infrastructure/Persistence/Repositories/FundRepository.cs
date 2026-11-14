using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Funds.Infrastructure;

public sealed class FundRepository(FundsDbContext dbContext) : IFundRepository
{
    public void Add(Fund fund) => dbContext.Funds.Add(fund);

    public void Remove(Fund fund) => dbContext.Funds.Remove(fund);

    public Task<Fund?> GetByIdAsync(FundId id, CancellationToken cancellationToken) =>
        dbContext.Funds.Include(f => f.Allocations).Include(f => f.LinkedReleases).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Fund>> GetActiveLinkedAsync(Guid? debtId, Guid? loanId, CancellationToken cancellationToken) =>
        await dbContext.Funds
            .Include(f => f.Allocations)
            .Include(f => f.LinkedReleases)
            .Where(f => f.Status == FundStatus.Active)
            .Where(f => (debtId != null && f.LinkedDebtId == debtId) || (loanId != null && f.LinkedLoanId == loanId))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Fund>> GetActiveWithLinkedReleaseAsync(OperationId operationId, CancellationToken cancellationToken) =>
        await dbContext.Funds
            .Include(f => f.Allocations)
            .Include(f => f.LinkedReleases)
            .Where(f => f.Status == FundStatus.Active)
            .Where(f => f.LinkedReleases.Any(r => r.OperationId == operationId && r.Amount != 0m))
            .OrderBy(f => f.CreatedAt)
            .ToListAsync(cancellationToken);

    public async Task<FundPageResult> ListAsync(
        bool includeClosed, FundCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Funds.Include(f => f.Allocations).Include(f => f.LinkedReleases).AsSplitQuery().AsQueryable();

        if (!includeClosed)
        {
            query = query.Where(f => f.Status == FundStatus.Active);
        }

        if (afterCursor is not null)
        {
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(f => f.CreatedAt > cursorCreatedAt);
        }

        var items = await query.OrderBy(f => f.CreatedAt).Take(limit + 1).ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new FundPageResult(items, hasMore);
    }

    public async Task<decimal> GetReservedOnWalletAsync(WalletId walletId, CancellationToken cancellationToken)
    {
        var reserved = await ActiveAllocations()
            .Where(a => a.WalletId == walletId)
            .SumAsync(a => a.Amount, cancellationToken);

        // An empty sum comes back as COALESCE(..., 0.0); a plain 0 keeps "balance − reserved" at the balance's scale.
        return reserved == 0m ? 0m : reserved;
    }

    public async Task<IReadOnlyList<WalletReservation>> GetReservedByWalletAsync(CancellationToken cancellationToken)
    {
        var rows = await (
                from allocation in dbContext.FundAllocations
                join fund in dbContext.Funds on allocation.FundId equals fund.Id
                where fund.Status == FundStatus.Active && allocation.Amount != 0m
                group allocation by new { allocation.WalletId, fund.CurrencyId } into g
                select new { g.Key.WalletId, g.Key.CurrencyId, Reserved = g.Sum(a => a.Amount) })
            .ToListAsync(cancellationToken);

        return rows.Select(r => new WalletReservation(r.WalletId, r.CurrencyId, r.Reserved)).ToList();
    }

    private IQueryable<FundAllocation> ActiveAllocations() =>
        from allocation in dbContext.FundAllocations
        join fund in dbContext.Funds on allocation.FundId equals fund.Id
        where fund.Status == FundStatus.Active
        select allocation;
}
