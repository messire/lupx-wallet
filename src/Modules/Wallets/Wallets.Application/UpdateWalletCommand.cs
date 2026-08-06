using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-02 (docs/api/openapi.yaml: PATCH /wallets/{id}, WalletUpdateRequest — без валюты,
/// смена валюты — отдельная команда ChangeWalletCurrencyCommand). По аналогии с
/// UpdateOperationCommand в модуле Operations — не частичный merge (несмотря на глагол
/// PATCH), все перечисленные поля передаются вызывающей стороной целиком, а не мержатся
/// с прежними значениями по признаку "не указано" (тот же прием, что уже принят для
/// Operations.UpdateOperationCommand). IncludeInTotal для основного кошелька
/// принудительно остаётся true — это не отказ (409), а тихое приведение (решено, Q8;
/// см. WalletUpdateRequest.includeInTotal в openapi.yaml: "Игнорируется... если кошелек
/// основной") — Wallet.UpdateDetails уже реализует это поведение, здесь не дублируется.
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
