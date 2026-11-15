using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

public sealed class OperationFactsLookup(OperationsDbContext dbContext) : IOperationFactsLookup
{
    public async Task<IReadOnlyList<OperationFact>> GetByIdsAsync(
        IReadOnlyCollection<OperationId> operationIds, CancellationToken cancellationToken)
    {
        if (operationIds.Count == 0)
        {
            return [];
        }

        var ids = operationIds.ToList();
        var rows = await dbContext.Operations.AsNoTracking()
            .Where(x => ids.Contains(x.Id))
            .ToListAsync(cancellationToken);
        return rows.Select(ToFact).ToList();
    }

    public async Task<IReadOnlyList<OperationFact>> ListInRangeAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken)
    {
        var rows = await dbContext.Operations.AsNoTracking()
            .Where(x => x.OperationDate >= from && x.OperationDate <= to
                && x.DeletedAt == null && !x.IsCorrection && x.TransferId == null)
            .ToListAsync(cancellationToken);
        return rows.Select(ToFact).ToList();
    }

    private static OperationFact ToFact(Operation x) => new(
        x.Id, x.WalletId, x.OperationTypeId, x.CurrencyId, x.Amount.Amount, x.AppliedDelta.Amount, x.OperationDate,
        IsTransferLeg: x.TransferId is not null, x.IsCorrection, x.IsDeleted);
}
