using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationRepository(OperationsDbContext dbContext) : IOperationRepository
{
    private const string AmountField = "_amount";
    private const string AppliedDeltaField = "_appliedDeltaAmount";

    public void Add(Operation operation) => dbContext.Operations.Add(operation);

    public Task<Operation?> GetByIdAsync(OperationId id, CancellationToken cancellationToken) =>
        dbContext.Operations.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<WalletId?> FindWalletIdAsync(OperationId id, CancellationToken cancellationToken)
    {
        var walletIds = await dbContext.Operations
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.WalletId)
            .ToListAsync(cancellationToken);

        return walletIds.Count == 0 ? null : walletIds[0];
    }

    public async Task<OperationPageResult> ListAsync(
        OperationListFilter filter, OperationCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        // A zero correction is not shown (ADR-0003, §6); GetByIdAsync still returns it.
        var query = dbContext.Operations
            .Where(x => !x.IsCorrection || EF.Property<decimal>(x, AmountField) != 0m);

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
            var cursorIsCorrection = afterCursor.IsCorrection;
            query = query.Where(x =>
                x.OperationDate > cursorDate ||
                (x.OperationDate == cursorDate && (
                    (x.IsCorrection && !cursorIsCorrection) ||
                    (x.IsCorrection == cursorIsCorrection && x.CreatedAt > cursorCreatedAt))));
        }

        var items = await query
            .OrderBy(x => x.OperationDate)
            .ThenBy(x => x.IsCorrection)
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

    public async Task<IReadOnlyList<Operation>> GetCorrectionsAsync(WalletId walletId, CancellationToken cancellationToken) =>
        await dbContext.Operations
            .Where(x => x.WalletId == walletId && x.IsCorrection)
            .OrderBy(x => x.OperationDate)
            .ToListAsync(cancellationToken);

    public Task<Operation?> GetCorrectionAsync(WalletId walletId, DateOnly operationDate, CancellationToken cancellationToken) =>
        dbContext.Operations.FirstOrDefaultAsync(
            x => x.WalletId == walletId && x.IsCorrection && x.OperationDate == operationDate, cancellationToken);

    public async Task<IReadOnlyList<WalletLedgerEntry>> GetRegularLedgerAsync(
        WalletId walletId, IReadOnlyCollection<OperationId> excludingIds, CancellationToken cancellationToken)
    {
        var query = dbContext.Operations
            .AsNoTracking()
            .Where(x => x.WalletId == walletId && !x.IsCorrection && x.DeletedAt == null);

        if (excludingIds.Count > 0)
        {
            // A List, not an array: with C# 14 (SDK 10+) Contains on an array binds to the ReadOnlySpan overload, which
            // EF Core cannot evaluate as a query parameter; List<T>.Contains is an instance method and is unaffected.
            var excluded = excludingIds.ToList();
            query = query.Where(x => !excluded.Contains(x.Id));
        }

        var rows = await query
            .Select(x => new { x.OperationDate, Delta = EF.Property<decimal>(x, AppliedDeltaField) })
            .ToListAsync(cancellationToken);

        return rows.Select(x => new WalletLedgerEntry(x.OperationDate, x.Delta)).ToList();
    }

    public Task<bool> AnyCorrectionsAsync(WalletId walletId, CancellationToken cancellationToken) =>
        dbContext.Operations.AnyAsync(x => x.WalletId == walletId && x.IsCorrection, cancellationToken);

    public async Task<IReadOnlyList<WalletId>> GetWalletIdsWithCorrectionsAsync(CancellationToken cancellationToken) =>
        await dbContext.Operations
            .Where(x => x.IsCorrection)
            .Select(x => x.WalletId)
            .Distinct()
            .ToListAsync(cancellationToken);

    public void RemoveCorrection(Operation correction)
    {
        if (!correction.IsCorrection)
        {
            throw new InvalidOperationException("Only a balance correction can be removed physically.");
        }

        dbContext.Operations.Remove(correction);
    }
}
