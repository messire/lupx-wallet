using LupexWallet.ReferenceData.Application;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class ReferenceDataUnitOfWork(ReferenceDataDbContext dbContext) : IReferenceDataUnitOfWork
{
    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
