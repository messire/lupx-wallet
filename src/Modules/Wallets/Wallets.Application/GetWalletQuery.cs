using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Wallets.Application;

/// <summary>GET /wallets/{id} (docs/api/openapi.yaml). Пропущено в первой версии среза
/// Wallets — обнаружено и добавлено при реализации среза Operations, которому эта
/// операция понадобилась для проверки эффекта на баланс.</summary>
public sealed record GetWalletQuery(Guid Id) : IRequest<WalletDto?>, IQuery<WalletDto?>;

public sealed class GetWalletQueryHandler(IWalletRepository repository) : IRequestHandler<GetWalletQuery, WalletDto?>
{
    public async Task<WalletDto?> Handle(GetWalletQuery request, CancellationToken cancellationToken)
    {
        var wallet = await repository.GetByIdAsync(new WalletId(request.Id), cancellationToken);
        return wallet is null ? null : WalletDto.FromDomain(wallet);
    }
}
