using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>UC-01 (docs/api/openapi.yaml: POST /wallets).</summary>
public sealed record CreateWalletCommand(
    string Name,
    Guid WalletTypeId,
    Guid CurrencyId,
    decimal InitialBalanceAmount,
    DateOnly AccountingStartDate,
    string? PurposeDescription,
    bool IncludeInTotal,
    int DisplayOrder,
    string? Color,
    string? Icon) : IRequest<WalletDto>, ICommand<WalletDto>;

/// <summary>
/// W2.5, case d: walletTypeId/currencyId are validated for existence and active status via
/// ReferenceData.Application.IWalletTypeLookup/ICurrencyLookup. Top-down direction
/// (Wallets.Application → ReferenceData.Application), no cycle — ADR-0009 does not apply
/// (same approach as Operations.Application's IOperationTypeLookup, ADR-0007).
/// </summary>
public sealed class CreateWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    PrimaryWalletPolicy primaryWalletPolicy,
    IWalletTypeLookup walletTypeLookup,
    ICurrencyLookup currencyLookup) : IRequestHandler<CreateWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(CreateWalletCommand request, CancellationToken cancellationToken)
    {
        var walletTypeId = new WalletTypeId(request.WalletTypeId);
        var walletType = await walletTypeLookup.GetAsync(walletTypeId, cancellationToken)
            ?? throw new WalletTypeReferenceNotFoundException(request.WalletTypeId);
        if (!walletType.IsActive)
        {
            throw new WalletTypeReferenceInactiveException(request.WalletTypeId);
        }

        var currencyId = new CurrencyId(request.CurrencyId);
        var currency = await currencyLookup.GetAsync(currencyId, cancellationToken)
            ?? throw new CurrencyReferenceNotFoundException(request.CurrencyId);
        if (!currency.IsActive)
        {
            throw new CurrencyReferenceInactiveException(request.CurrencyId);
        }

        var isFirstWallet = await primaryWalletPolicy.IsFirstWalletAsync(cancellationToken);

        var wallet = Wallet.Create(
            WalletId.New(),
            request.Name,
            walletTypeId,
            new Money(request.InitialBalanceAmount, currencyId),
            request.AccountingStartDate,
            request.PurposeDescription,
            request.IncludeInTotal,
            request.DisplayOrder,
            request.Color,
            request.Icon,
            isFirstWallet);

        repository.Add(wallet);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
