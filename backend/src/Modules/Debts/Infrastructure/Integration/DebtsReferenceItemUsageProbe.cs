using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Debts.Infrastructure;

/// <summary>
/// ADR-0002 §4: a currency referenced by a debt or a loan (cancelled ones included) is in use and
/// cannot be removed from the active set. Operation types used by repayments are already covered
/// by Operations' probe (the repayment is an operation).
/// </summary>
public sealed class DebtsReferenceItemUsageProbe(DebtsDbContext dbContext) : IReferenceItemUsageProbe
{
    public async Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken)
    {
        if (kind != ReferenceItemKind.Currency)
        {
            return false;
        }

        var currencyId = new CurrencyId(referenceItemId);
        return await dbContext.Debts.AnyAsync(x => x.CurrencyId == currencyId, cancellationToken)
            || await dbContext.Loans.AnyAsync(x => x.CurrencyId == currencyId, cancellationToken);
    }
}
