using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>GET /funds/wallet-reservations: balance, reserved, free and deficit of every wallet.</summary>
public sealed record ListWalletReservationsQuery : IRequest<IReadOnlyList<WalletReservationDto>>, IQuery<IReadOnlyList<WalletReservationDto>>;

public sealed class ListWalletReservationsQueryHandler(FundViewBuilder viewBuilder)
    : IRequestHandler<ListWalletReservationsQuery, IReadOnlyList<WalletReservationDto>>
{
    public Task<IReadOnlyList<WalletReservationDto>> Handle(ListWalletReservationsQuery request, CancellationToken cancellationToken) =>
        viewBuilder.BuildWalletSummariesAsync(cancellationToken);
}

/// <summary>GET /funds/wallet-reservations/{walletId}.</summary>
public sealed record GetWalletReservationQuery(Guid WalletId) : IRequest<WalletReservationDto?>, IQuery<WalletReservationDto?>;

public sealed class GetWalletReservationQueryHandler(FundViewBuilder viewBuilder)
    : IRequestHandler<GetWalletReservationQuery, WalletReservationDto?>
{
    public Task<WalletReservationDto?> Handle(GetWalletReservationQuery request, CancellationToken cancellationToken) =>
        viewBuilder.BuildWalletSummaryAsync(new WalletId(request.WalletId), cancellationToken);
}
