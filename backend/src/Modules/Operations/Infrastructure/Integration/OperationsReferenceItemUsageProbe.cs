using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// ADR-0009, case c — one of the implementations of
/// ReferenceData.Application.IReferenceItemUsageProbe: data owner is Operations. Checks
/// OperationType/Currency usage in operations.operations (a targeted EXISTS, not a collection
/// dump). WalletType is not relevant to this module — returns false. Registered in
/// AddOperationsModule.
/// </summary>
public sealed class OperationsReferenceItemUsageProbe(OperationsDbContext dbContext) : IReferenceItemUsageProbe
{
    public Task<bool> IsUsedAsync(ReferenceItemKind kind, Guid referenceItemId, CancellationToken cancellationToken) =>
        kind switch
        {
            ReferenceItemKind.OperationType => dbContext.Operations.AnyAsync(
                o => o.OperationTypeId == new OperationTypeId(referenceItemId), cancellationToken),
            ReferenceItemKind.Currency => dbContext.Operations.AnyAsync(
                o => o.CurrencyId == new CurrencyId(referenceItemId), cancellationToken),
            ReferenceItemKind.WalletType => Task.FromResult(false),
            _ => Task.FromResult(false),
        };
}
