using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>
/// ADR-0010 §4, B9: a payment of debt/loan X from wallet W releases the reserve of the active
/// fund(s) linked to X on W, in total not more than <see cref="Amount"/> and not more than each
/// allocation. Several linked funds are released oldest first. Returns the released total.
/// Meant for the future subscriber to the Debts payment events; no HTTP endpoint.
/// </summary>
public sealed record ReleaseLinkedReserveCommand(Guid? DebtId, Guid? LoanId, Guid WalletId, decimal Amount)
    : IRequest<decimal>, ICommand<decimal>;

public sealed class ReleaseLinkedReserveCommandHandler(IFundRepository repository, IFundsUnitOfWork unitOfWork)
    : IRequestHandler<ReleaseLinkedReserveCommand, decimal>
{
    public async Task<decimal> Handle(ReleaseLinkedReserveCommand request, CancellationToken cancellationToken)
    {
        if ((request.DebtId is null) == (request.LoanId is null))
        {
            throw new FundLinkAmbiguousException();
        }

        if (request.Amount <= 0m)
        {
            throw new FundAmountMustBePositiveException();
        }

        var walletId = new WalletId(request.WalletId);
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var funds = await repository.GetActiveLinkedAsync(request.DebtId, request.LoanId, cancellationToken);

        var remaining = request.Amount;
        foreach (var fund in funds)
        {
            if (remaining == 0m)
            {
                break;
            }

            remaining -= fund.ReleaseUpTo(walletId, remaining);
        }

        var released = request.Amount - remaining;
        if (released > 0m)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return released;
    }
}
