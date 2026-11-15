using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>POST /funds/{id}/release: decreases the allocation on a wallet by the amount (ADR-0010 §4).</summary>
public sealed record ReleaseFundCommand(Guid FundId, Guid WalletId, decimal Amount) : IRequest<FundDto>, ICommand<FundDto>;

public sealed class ReleaseFundCommandHandler(
    IFundRepository repository,
    IFundsUnitOfWork unitOfWork,
    FundViewBuilder viewBuilder) : IRequestHandler<ReleaseFundCommand, FundDto>
{
    public async Task<FundDto> Handle(ReleaseFundCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var fund = await repository.GetByIdAsync(new FundId(request.FundId), cancellationToken)
            ?? throw new FundNotFoundException();

        fund.Release(walletId, request.Amount);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await viewBuilder.BuildAsync(fund, cancellationToken);
    }
}
