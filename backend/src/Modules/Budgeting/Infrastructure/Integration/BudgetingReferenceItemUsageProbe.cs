using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Budgeting.Infrastructure;

/// <summary>
/// ADR-0002 §4, ADR-0010 §5: Budgeting implementation of IReferenceItemUsageProbe. An operation type
/// is used while it belongs to an expense category (active or not); a currency is used by a budget.
/// WalletType is not relevant here.
/// </summary>
public sealed class BudgetingReferenceItemUsageProbe(BudgetingDbContext dbContext) : IReferenceItemUsageProbe
{
    public Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken) =>
        kind switch
        {
            ReferenceItemKind.OperationType => dbContext.ExpenseCategories.AnyAsync(
                c => c.Members.Any(m => m.OperationTypeId == new OperationTypeId(referenceItemId)), cancellationToken),
            ReferenceItemKind.Currency => dbContext.Budgets.AnyAsync(
                b => b.CurrencyId == new CurrencyId(referenceItemId), cancellationToken),
            _ => Task.FromResult(false),
        };
}
