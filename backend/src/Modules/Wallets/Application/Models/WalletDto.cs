using LupexWallet.Wallets.Domain;

namespace LupexWallet.Wallets.Application;

public sealed record WalletDto(
    Guid Id,
    string Name,
    Guid WalletTypeId,
    string? PurposeDescription,
    Guid CurrencyId,
    decimal InitialBalanceAmount,
    DateOnly AccountingStartDate,
    decimal CurrentBalanceAmount,
    bool IncludeInTotal,
    bool IsPrimary,
    bool IsArchived,
    int DisplayOrder,
    string? Color,
    string? Icon,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static WalletDto FromDomain(Wallet wallet) => new(
        wallet.Id.Value,
        wallet.Name,
        wallet.WalletTypeId.Value,
        wallet.PurposeDescription,
        wallet.CurrencyId.Value,
        wallet.InitialBalance.Amount,
        wallet.AccountingStartDate,
        wallet.CurrentBalance.Amount,
        wallet.IncludeInTotal,
        wallet.IsPrimary,
        wallet.IsArchived,
        wallet.DisplayOrder,
        wallet.Color,
        wallet.Icon,
        wallet.CreatedAt,
        wallet.UpdatedAt);
}
