using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class OperationBehaviorKindRepository(ReferenceDataDbContext dbContext) : IOperationBehaviorKindRepository
{
    public async Task<IReadOnlyList<OperationBehaviorKind>> ListAllAsync(CancellationToken cancellationToken) =>
        await dbContext.OperationBehaviorKinds.OrderBy(x => x.Name).ToListAsync(cancellationToken);
}
