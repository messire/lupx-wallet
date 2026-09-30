using LupexWallet.Operations.Domain;

namespace LupexWallet.Operations.Application;

public sealed record TransferDto(
    Guid Id,
    Guid SourceWalletId,
    Guid TargetWalletId,
    Guid SourceOperationId,
    Guid TargetOperationId,
    decimal Amount,
    Guid CurrencyId,
    DateOnly TransferDate,
    DateTimeOffset CreatedAt,
    bool IsDeleted,
    DateTimeOffset? DeletedAt)
{
    public static TransferDto FromDomain(Transfer transfer) => new(
        transfer.Id.Value,
        transfer.SourceWalletId.Value,
        transfer.TargetWalletId.Value,
        transfer.SourceOperationId.Value,
        transfer.TargetOperationId.Value,
        transfer.Amount.Amount,
        transfer.CurrencyId.Value,
        transfer.TransferDate,
        transfer.CreatedAt,
        transfer.IsDeleted,
        transfer.DeletedAt);
}
