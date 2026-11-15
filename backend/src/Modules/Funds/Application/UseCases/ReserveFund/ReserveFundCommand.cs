using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>POST /funds/{id}/reserve: creates or increases the allocation on a wallet (ADR-0010 §4, B7).</summary>
public sealed record ReserveFundCommand(Guid FundId, Guid WalletId, decimal Amount) : IRequest<FundDto>, ICommand<FundDto>;

public sealed class ReserveFundCommandHandler(
    IFundRepository repository,
    IFundsUnitOfWork unitOfWork,
    ReservationPolicy reservationPolicy,
    FundViewBuilder viewBuilder) : IRequestHandler<ReserveFundCommand, FundDto>
{
    public async Task<FundDto> Handle(ReserveFundCommand request, CancellationToken cancellationToken)
    {
        var walletId = new WalletId(request.WalletId);

        // Serializes all reserves on this wallet (and wallet archive/delete), see ReservationPolicy.
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var fund = await repository.GetByIdAsync(new FundId(request.FundId), cancellationToken)
            ?? throw new FundNotFoundException();

        await reservationPolicy.ReserveAsync(fund, walletId, request.Amount, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await viewBuilder.BuildAsync(fund, cancellationToken);
    }
}
