using LupexWallet.Debts.Application;
using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Debts.Infrastructure;

public sealed class CreditorRepository(DebtsDbContext dbContext) : ICreditorRepository
{
    public void Add(Creditor creditor) => dbContext.Creditors.Add(creditor);

    public void Remove(Creditor creditor) => dbContext.Creditors.Remove(creditor);

    public Task<Creditor?> GetByIdAsync(CreditorId id, CancellationToken cancellationToken) =>
        dbContext.Creditors.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Creditor>> GetByIdsAsync(IReadOnlyCollection<CreditorId> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var list = ids.ToList();
        return await dbContext.Creditors.AsNoTracking().Where(x => list.Contains(x.Id)).ToListAsync(cancellationToken);
    }

    public async Task<PageResult<Creditor>> ListAsync(
        bool includeInactive, CreditorCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Creditors.AsNoTracking();
        if (!includeInactive)
        {
            query = query.Where(x => x.IsActive);
        }

        if (afterCursor is not null)
        {
            var name = afterCursor.Name;
            var createdAt = afterCursor.CreatedAt;
            query = query.Where(x => string.Compare(x.Name, name) > 0 || (x.Name == name && x.CreatedAt > createdAt));
        }

        var items = await query.OrderBy(x => x.Name).ThenBy(x => x.CreatedAt).Take(limit + 1).ToListAsync(cancellationToken);
        return Paging.ToPage(items, limit);
    }

    public async Task<bool> IsUsedAsync(CreditorId id, CancellationToken cancellationToken) =>
        await dbContext.Debts.AnyAsync(x => x.CreditorId == id, cancellationToken)
        || await dbContext.Loans.AnyAsync(x => x.CreditorId == id, cancellationToken);
}

public sealed class DebtRepository(DebtsDbContext dbContext) : IDebtRepository
{
    public void Add(Debt debt) => dbContext.Debts.Add(debt);

    public void Remove(Debt debt) => dbContext.Debts.Remove(debt);

    public Task<Debt?> GetByIdAsync(DebtId id, CancellationToken cancellationToken) =>
        dbContext.Debts.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Debt?> GetForUpdateAsync(DebtId id, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM debts.debts WHERE id = {id.Value} FOR UPDATE", cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<PageResult<Debt>> ListAsync(
        CreditorId? creditorId, DateTimeOffset? afterCreatedAt, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Debts.AsNoTracking();
        if (creditorId is { } c)
        {
            query = query.Where(x => x.CreditorId == c);
        }

        if (afterCreatedAt is { } after)
        {
            query = query.Where(x => x.CreatedAt > after);
        }

        var items = await query.OrderBy(x => x.CreatedAt).Take(limit + 1).ToListAsync(cancellationToken);
        return Paging.ToPage(items, limit);
    }

    public async Task<IReadOnlyList<Debt>> ListNotCancelledAsync(CancellationToken cancellationToken) =>
        await dbContext.Debts.AsNoTracking().Where(x => x.CancelledAt == null).ToListAsync(cancellationToken);
}

public sealed class LoanRepository(DebtsDbContext dbContext) : ILoanRepository
{
    public void Add(Loan loan) => dbContext.Loans.Add(loan);

    public void Remove(Loan loan) => dbContext.Loans.Remove(loan);

    public Task<Loan?> GetByIdAsync(LoanId id, CancellationToken cancellationToken) =>
        dbContext.Loans.AsSplitQuery().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<Loan?> GetForUpdateAsync(LoanId id, CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT 1 FROM debts.loans WHERE id = {id.Value} FOR UPDATE", cancellationToken);
        return await GetByIdAsync(id, cancellationToken);
    }

    public async Task<PageResult<Loan>> ListAsync(
        CreditorId? creditorId, DateTimeOffset? afterCreatedAt, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Loans.AsNoTracking().AsSplitQuery();
        if (creditorId is { } c)
        {
            query = query.Where(x => x.CreditorId == c);
        }

        if (afterCreatedAt is { } after)
        {
            query = query.Where(x => x.CreatedAt > after);
        }

        var items = await query.OrderBy(x => x.CreatedAt).Take(limit + 1).ToListAsync(cancellationToken);
        return Paging.ToPage(items, limit);
    }
}

public sealed class DebtsUnitOfWork(DebtsDbContext dbContext) : IDebtsUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}

internal static class Paging
{
    public static PageResult<T> ToPage<T>(List<T> items, int limit)
    {
        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new PageResult<T>(items, hasMore);
    }
}
