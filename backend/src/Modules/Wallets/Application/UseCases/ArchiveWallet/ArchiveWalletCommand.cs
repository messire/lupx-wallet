using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>UC-03 (docs/api/openapi.yaml: POST /wallets/{id}/archive). The primary wallet cannot be archived (Q7) — Wallet.Archive itself throws CannotArchivePrimaryWalletException.
/// A wallet holding a reserve of an active fund cannot be archived either (ADR-0010 §4, IWalletReservationProbe).</summary>
public sealed record ArchiveWalletCommand(Guid Id) : IRequest<WalletDto>, ICommand<WalletDto>;

public sealed class ArchiveWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    IEnumerable<IWalletReservationProbe> reservationProbes) : IRequestHandler<ArchiveWalletCommand, WalletDto>
{
    public async Task<WalletDto> Handle(ArchiveWalletCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.Id);

        // Funds takes the same lock before reserving, so a reserve cannot slip in after the check.
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var wallet = await repository.GetByIdAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException();

        if (await reservationProbes.AnyHasActiveReservationsAsync(walletId, cancellationToken))
        {
            throw new WalletHasActiveReservationsException();
        }

        wallet.Archive();

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return WalletDto.FromDomain(wallet);
    }
}
