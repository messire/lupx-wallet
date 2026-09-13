using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-02 (docs/api/openapi.yaml: PATCH /wallets/{id}, WalletUpdateRequest — no currency,
/// that's a separate ChangeWalletCurrencyCommand). Not a partial merge despite the PATCH
/// verb — all listed fields are passed in full by the caller, same convention as
/// Operations.UpdateOperationCommand. IncludeInTotal stays true for the primary wallet
/// (silent coercion, not a 409; Q8) — already implemented by Wallet.UpdateDetails.
/// </summary>
public sealed record UpdateWalletCommand(
    Guid Id,
    string Name,
    Guid WalletTypeId,
    string? PurposeDescription,
    bool IncludeInTotal,
    int DisplayOrder,
    string? Color,
    string? Icon) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class UpdateWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    IWalletTypeLookup walletTypeLookup) : IRequestHandler<UpdateWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(UpdateWalletCommand request, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(new WalletId(request.Id), cancellationToken)
            ?? throw new WalletNotFoundException(request.Id);

        // W2.5, случай d: walletTypeId проверяется на существование и активность.
        var walletTypeId = new WalletTypeId(request.WalletTypeId);
        var walletType = await walletTypeLookup.GetAsync(walletTypeId, cancellationToken)
            ?? throw new WalletTypeReferenceNotFoundException(request.WalletTypeId);
        if (!walletType.IsActive)
        {
            throw new WalletTypeReferenceInactiveException(request.WalletTypeId);
        }

        wallet.UpdateDetails(
            request.Name,
            walletTypeId,
            request.PurposeDescription,
            request.IncludeInTotal,
            request.DisplayOrder,
            request.Color,
            request.Icon);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
