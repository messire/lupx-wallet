using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.ReferenceData.Infrastructure;

public sealed class WalletTypeLookup(ReferenceDataDbContext dbContext) : IWalletTypeLookup
{
    public async Task<WalletTypeLookupResult?> GetAsync(WalletTypeId id, CancellationToken cancellationToken)
    {
        var walletType = await dbContext.WalletTypes.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        return walletType is null ? null : new WalletTypeLookupResult(walletType.Id, walletType.Name, walletType.IsActive);
    }
}
