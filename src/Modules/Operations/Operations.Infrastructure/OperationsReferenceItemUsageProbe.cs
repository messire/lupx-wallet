using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace LupexWallet.Operations.Infrastructure;

/// <summary>
/// ADR-0009, случай c — одна из реализаций ReferenceData.Application.IReferenceItemUsageProbe:
/// владелец данных Operations. Проверяет OperationType/Currency по факту использования в
/// operations.operations (точечный EXISTS, не выгрузка коллекций). WalletType этому модулю
/// не релевантен — возвращает false. Зарегистрирована в AddOperationsModule.
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
