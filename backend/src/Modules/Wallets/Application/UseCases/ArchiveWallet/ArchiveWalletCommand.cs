using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>UC-03 (docs/api/openapi.yaml: POST /wallets/{id}/archive). The primary wallet cannot be archived (Q7) — Wallet.Archive itself throws CannotArchivePrimaryWalletException.</summary>
public sealed record ArchiveWalletCommand(Guid Id) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class ArchiveWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork) : IRequestHandler<ArchiveWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(ArchiveWalletCommand request, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(new WalletId(request.Id), cancellationToken)
            ?? throw new WalletNotFoundException(request.Id);

        wallet.Archive();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
