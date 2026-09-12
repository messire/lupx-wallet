using LupexWallet.Operations.Domain;

namespace LupexWallet.Operations.Application;

public sealed record OperationDto(
    Guid Id,
    Guid WalletId,
    Guid OperationTypeId,
    decimal Amount,
    Guid CurrencyId,
    DateOnly OperationDate,
    string? AdjustmentMode,
    Guid? TransferId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static OperationDto FromDomain(Operation operation) => new(
        operation.Id.Value,
        operation.WalletId.Value,
        operation.OperationTypeId.Value,
        operation.Amount.Amount,
        operation.CurrencyId.Value,
        operation.OperationDate,
        operation.AdjustmentMode?.ToString(),
        operation.TransferId?.Value,
        operation.CreatedAt,
        operation.UpdatedAt);
}
