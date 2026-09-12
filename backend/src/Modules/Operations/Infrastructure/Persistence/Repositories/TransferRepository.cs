using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class TransferRepository(OperationsDbContext dbContext) : ITransferRepository
{
    public void Add(Transfer transfer) => dbContext.Transfers.Add(transfer);

    public void Remove(Transfer transfer) => dbContext.Transfers.Remove(transfer);

    public Task<Transfer?> GetByIdAsync(TransferId id, CancellationToken cancellationToken) =>
        dbContext.Transfers.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<TransferPageResult> ListAsync(
        WalletId? walletId, TransferCursor? afterCursor, int limit, CancellationToken cancellationToken)
    {
        var query = dbContext.Transfers.AsQueryable();

        if (walletId is { } id)
        {
            query = query.Where(x => x.SourceWalletId == id || x.TargetWalletId == id);
        }

        if (afterCursor is not null)
        {
            var cursorDate = afterCursor.TransferDate;
            var cursorCreatedAt = afterCursor.CreatedAt;
            query = query.Where(x =>
                x.TransferDate > cursorDate ||
                (x.TransferDate == cursorDate && x.CreatedAt > cursorCreatedAt));
        }

        var items = await query
            .OrderBy(x => x.TransferDate)
            .ThenBy(x => x.CreatedAt)
            .Take(limit + 1)
            .ToListAsync(cancellationToken);

        var hasMore = items.Count > limit;
        if (hasMore)
        {
            items.RemoveAt(items.Count - 1);
        }

        return new TransferPageResult(items, hasMore);
    }
}
