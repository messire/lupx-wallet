using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-05 (docs/api/openapi.yaml: POST /wallets/{id}/set-primary) — через
/// PrimaryWalletPolicy.SetPrimaryAsync: снимает признак "основной" с прежнего основного
/// кошелька (если это другой кошелек), назначает целевой (которое само бросает
/// CannotSetArchivedWalletAsPrimaryException для архивного кошелька — здесь не
/// дублируется); include_in_total принудительно становится true (решено, Q8).
/// </summary>
public sealed record SetPrimaryWalletCommand(Guid Id) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class SetPrimaryWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    PrimaryWalletPolicy primaryWalletPolicy) : IRequestHandler<SetPrimaryWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(SetPrimaryWalletCommand request, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(new WalletId(request.Id), cancellationToken)
            ?? throw new WalletNotFoundException(request.Id);

        await primaryWalletPolicy.SetPrimaryAsync(wallet, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
