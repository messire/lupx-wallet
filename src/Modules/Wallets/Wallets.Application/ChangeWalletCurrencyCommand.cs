using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-06 (docs/api/openapi.yaml: PUT /wallets/{id}/currency) — смена валюты разрешена
/// только если у кошелька нет истории (решено, Q15; ADR-0009, случай a — тот же
/// IWalletHistorySource, что и DeleteWalletCommand).
/// </summary>
public sealed record ChangeWalletCurrencyCommand(Guid Id, Guid CurrencyId) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class ChangeWalletCurrencyCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    IEnumerable<IWalletHistorySource> historySources) : IRequestHandler<ChangeWalletCurrencyCommand, WalletDto>
{
    public async Task<WalletDto> Handle(ChangeWalletCurrencyCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.Id);
        var wallet = await repository.GetByIdAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException(request.Id);

        var hasHistory = await historySources.AnyHasHistoryAsync(walletId, cancellationToken);

        wallet.ChangeCurrency(new CurrencyId(request.CurrencyId), hasHistory);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
