using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Domain;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>
/// UC-04 (docs/api/openapi.yaml: DELETE /wallets/{id}) — физическое удаление разрешено
/// только если у кошелька нет истории (ADR-0009, случай a: IWalletHistorySource
/// агрегирует ответ владельцев данных Operations и BalanceHistory) и он не основной
/// (решено, Q7 — проверяется самим Wallet.EnsureCanBeDeleted).
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
            ?? throw new WalletNotFoundException(request.Id);

        var hasHistory = await historySources.AnyHasHistoryAsync(walletId, cancellationToken);

        wallet.EnsureCanBeDeleted(hasHistory);

        wallet.MarkAsDeleted();
        repository.Remove(wallet);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
