using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>
/// ADR-0010 §4, B9: the expense of a linked debt/loan payment was soft-deleted — the reserve it
/// released comes back to the still active funds, oldest fund first, within the wallet's free funds
/// (balance − reserves of active funds, after the deletion). What does not fit stays released.
/// Returns the restored total. Sent by the Funds subscriber to OperationDeleted; no HTTP endpoint.
/// </summary>
public sealed record RestoreLinkedReserveCommand(Guid OperationId) : IRequest<decimal>, ICommand<decimal>;

public sealed class RestoreLinkedReserveCommandHandler(
    IFundRepository repository,
    IFundsUnitOfWork unitOfWork,
    IWalletBalanceGateway walletBalanceGateway) : IRequestHandler<RestoreLinkedReserveCommand, decimal>
{
    public async Task<decimal> Handle(RestoreLinkedReserveCommand request, CancellationToken cancellationToken)
    {
        var operationId = new OperationId(request.OperationId);
        var funds = await repository.GetActiveWithLinkedReleaseAsync(operationId, cancellationToken);
        if (funds.Count == 0)
        {
            return 0m;
        }

        var walletIds = funds
            .SelectMany(f => f.LinkedReleases.Where(r => r.OperationId == operationId && r.Amount > 0m).Select(r => r.WalletId))
            .Distinct()
            .OrderBy(w => w.Value)
            .ToList();

        var restored = 0m;
        foreach (var walletId in walletIds)
        {
            await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

            var wallet = await walletBalanceGateway.GetAsync(walletId, cancellationToken);
            if (wallet is null)
            {
                continue;
            }

            var reserved = await repository.GetReservedOnWalletAsync(walletId, cancellationToken);
            var free = ReservationCoverage.Free(wallet.CurrentBalance.Amount, reserved);

            // funds come oldest first (GetActiveWithLinkedReleaseAsync).
            foreach (var fund in funds)
            {
                if (free <= 0m)
                {
                    break;
                }

                var amount = fund.RestoreLinkedRelease(operationId, walletId, free);
                free -= amount;
                restored += amount;
            }
        }

        if (restored > 0m)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return restored;
    }
}
