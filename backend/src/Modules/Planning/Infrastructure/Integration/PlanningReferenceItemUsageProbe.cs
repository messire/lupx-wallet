using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Planning.Infrastructure;

/// <summary>
/// ADR-0002 §4: implementation of ReferenceData.Application.IReferenceItemUsageProbe for Planning.
/// An operation type or a currency of any planned transaction (finished included) counts as used.
/// WalletType is not relevant here.
/// </summary>
public sealed class PlanningReferenceItemUsageProbe(PlanningDbContext dbContext) : IReferenceItemUsageProbe
{
    public Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken) =>
        kind switch
        {
            ReferenceItemKind.OperationType => dbContext.PlannedTransactions.AnyAsync(
                p => p.OperationTypeId == new OperationTypeId(referenceItemId), cancellationToken),
            ReferenceItemKind.Currency => dbContext.PlannedTransactions.AnyAsync(
                p => p.CurrencyId == new CurrencyId(referenceItemId), cancellationToken),
            _ => Task.FromResult(false),
        };
}
