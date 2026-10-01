using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-04 (docs/api/openapi.yaml: DELETE /wallets/{id}) — hard delete is allowed only if the
/// wallet has no history and it is not primary (Q7, checked by Wallet.EnsureCanBeDeleted).
/// History = operations, including soft-deleted ones (ADR-0002, §2; IWalletHistorySource, ADR-0001,
/// §2, way d). Snapshots are not history: BalanceHistory removes them in response to WalletDeleted,
/// in the same transaction (WalletDeletedHandler).
/// </summary>
public sealed record DeleteWalletCommand(Guid Id) : IRequest, ICommand<Unit>;

public sealed class DeleteWalletCommandHandler(
    IWalletRepository repository,
    IWalletsUnitOfWork unitOfWork,
    IEnumerable<IWalletHistorySource> historySources) : IRequestHandler<DeleteWalletCommand>
{
    public async Task Handle(DeleteWalletCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.Id);
        var wallet = await repository.GetByIdAsync(walletId, cancellationToken)
            ?? throw new WalletNotFoundException();

        var hasHistory = await historySources.AnyHasHistoryAsync(walletId, cancellationToken);

        wallet.EnsureCanBeDeleted(hasHistory);

        wallet.MarkAsDeleted();
        repository.Remove(wallet);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
