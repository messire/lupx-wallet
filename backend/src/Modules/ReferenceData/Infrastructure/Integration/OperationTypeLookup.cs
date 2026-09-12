using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class OperationTypeLookup(ReferenceDataDbContext dbContext) : IOperationTypeLookup
{
    public async Task<OperationTypeLookupResult?> GetAsync(OperationTypeId id, CancellationToken cancellationToken)
    {
        var entity = await dbContext.OperationTypes
            .Join(dbContext.OperationBehaviorKinds, ot => ot.BehaviorKindId, bk => bk.Id, (ot, bk) => new { ot, bk })
            .FirstOrDefaultAsync(x => x.ot.Id == id, cancellationToken);

        return entity is null
            ? null
            : new OperationTypeLookupResult(entity.ot.Id, entity.ot.Name, entity.ot.BehaviorKindId, entity.bk.Code, entity.ot.IsActive);
    }

    public async Task<OperationTypeLookupResult?> FindActiveByBehaviorKindCodeAsync(
        string behaviorKindCode, CancellationToken cancellationToken)
    {
        var entity = await dbContext.OperationTypes
            .Join(dbContext.OperationBehaviorKinds, ot => ot.BehaviorKindId, bk => bk.Id, (ot, bk) => new { ot, bk })
            .Where(x => x.bk.Code == behaviorKindCode && x.ot.IsActive)
            .OrderBy(x => x.ot.Name)
            .FirstOrDefaultAsync(cancellationToken);

        return entity is null
            ? null
            : new OperationTypeLookupResult(entity.ot.Id, entity.ot.Name, entity.ot.BehaviorKindId, entity.bk.Code, entity.ot.IsActive);
    }
}
