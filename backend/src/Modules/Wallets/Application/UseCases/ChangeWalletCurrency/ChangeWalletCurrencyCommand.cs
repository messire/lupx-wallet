using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-06 (docs/api/openapi.yaml: PUT /wallets/{id}/currency) — currency change is allowed only if the wallet has no history
/// (Q15; ADR-0002, §2: operations including soft-deleted ones, same IWalletHistorySource as DeleteWalletCommand).
/// Snapshots are not history: BalanceHistory rebuilds them in the new currency in response to
/// WalletCurrencyChanged (WalletCurrencyChangedHandler). The new currency must be active, i.e. present
/// in ReferenceData (ICurrencyLookup); an unknown id is a validation error.
/// </summary>
public sealed record ChangeWalletCurrencyCommand(Guid Id, Guid CurrencyId) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class ChangeWalletCurrencyCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    IEnumerable<IWalletHistorySource> historySources,
    ICurrencyLookup currencyLookup) : IRequestHandler<ChangeWalletCurrencyCommand, WalletDto>
{
    public async Task<WalletDto> Handle(ChangeWalletCurrencyCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.Id);

        // The subscribers take this lock in the same transaction; taking it after the wallet row
        // would invert the order used by Operations commands (lock, then row) and deadlock.
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var wallet = await repository.GetByIdAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException();

        var currencyId = new CurrencyId(request.CurrencyId);
        _ = await currencyLookup.GetAsync(currencyId, cancellationToken)
            ?? throw new CurrencyReferenceNotFoundException();

        var hasHistory = await historySources.AnyHasHistoryAsync(walletId, cancellationToken);

        wallet.ChangeCurrency(currencyId, hasHistory);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
