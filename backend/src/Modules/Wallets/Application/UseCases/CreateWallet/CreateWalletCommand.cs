using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>UC-01 (docs/api/openapi.yaml: POST /wallets).</summary>
public sealed record CreateWalletCommand(
    string Name,
    Guid WalletTypeId,
    string CurrencyCode,
    decimal InitialBalanceAmount,
    DateOnly AccountingStartDate,
    string? PurposeDescription,
    bool IncludeInTotal,
    int DisplayOrder,
    string? Color,
    string? Icon) : IRequest<WalletDto>, ICommand<WalletDto>;

/// <summary>
/// walletTypeId is validated for existence and active status via
/// ReferenceData.Application.IWalletTypeLookup. The currency is given by code and resolved through
/// ICurrencyActivation: an available but inactive currency becomes active in this command's
/// transaction (ADR-0004 §7), so a later failure rolls the activation back. Top-down direction
/// (Wallets.Application → ReferenceData.Application), no cycle (ADR-0001).
/// </summary>
public sealed class CreateWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    PrimaryWalletPolicy primaryWalletPolicy,
    IWalletTypeLookup walletTypeLookup,
    ICurrencyActivation currencyActivation) : IRequestHandler<CreateWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(CreateWalletCommand request, CancellationToken cancellationToken)
    {
        var walletTypeId = new WalletTypeId(request.WalletTypeId);
        var walletType = await walletTypeLookup.GetAsync(walletTypeId, cancellationToken)
            ?? throw new WalletTypeReferenceNotFoundException();
        if (!walletType.IsActive)
        {
            throw new WalletTypeReferenceInactiveException();
        }

        var currency = await currencyActivation.EnsureActiveAsync(request.CurrencyCode, cancellationToken)
            ?? throw new CurrencyReferenceNotFoundException();
        var currencyId = currency.Id;

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
