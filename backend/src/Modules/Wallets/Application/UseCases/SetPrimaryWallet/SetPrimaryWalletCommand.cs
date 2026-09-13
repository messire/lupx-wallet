using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-05 (docs/api/openapi.yaml: POST /wallets/{id}/set-primary) — via
/// PrimaryWalletPolicy.SetPrimaryAsync: unmarks the previous primary wallet (if different)
/// and marks the target (which itself throws CannotSetArchivedWalletAsPrimaryException for
/// an archived wallet); include_in_total is forced to true (Q8).
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
