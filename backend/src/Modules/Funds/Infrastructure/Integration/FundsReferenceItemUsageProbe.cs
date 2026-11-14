using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>
/// ADR-0002 §4: a Funds-owned implementation of IReferenceItemUsageProbe — a currency is used by
/// any fund in it (closed ones included, they keep their history).
/// </summary>
public sealed class FundsReferenceItemUsageProbe(FundsDbContext dbContext) : IReferenceItemUsageProbe
{
    public Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken) =>
        kind == ReferenceItemKind.Currency
            ? dbContext.Funds.AnyAsync(f => f.CurrencyId == new CurrencyId(referenceItemId), cancellationToken)
            : Task.FromResult(false);
}
