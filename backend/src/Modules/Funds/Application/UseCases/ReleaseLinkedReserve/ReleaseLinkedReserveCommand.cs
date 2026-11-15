using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Funds.Application;

/// <summary>
/// ADR-0010 §4, B9: a payment of debt/loan X from wallet W releases the reserve of the active
/// fund(s) linked to X on W, in total not more than <see cref="Amount"/> and not more than each
/// allocation. Several linked funds are released oldest first. What each fund released is kept per
/// <see cref="OperationId"/>, so deleting the expense can bring it back
/// (<see cref="RestoreLinkedReserveCommand"/>). Returns the released total. Sent by the Funds
/// subscribers to DebtRepaymentRecorded / LoanPaymentRecorded / OperationRestored; no HTTP endpoint.
/// </summary>
public sealed record ReleaseLinkedReserveCommand(Guid? DebtId, Guid? LoanId, Guid WalletId, decimal Amount, Guid OperationId)
    : IRequest<decimal>, ICommand<decimal>
{
    /// <summary>
    /// Amount of the payment itself when it differs from <see cref="Amount"/> (a restored payment releases less,
    /// review v0.4.0 R3). What a fund keeps as released by the operation never exceeds it (R4). Null — <see cref="Amount"/>.
    /// </summary>
    public decimal? PaymentAmount { get; init; }
}

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
        var operationId = new OperationId(request.OperationId);
        await unitOfWork.AcquireWalletLockAsync(walletId, cancellationToken);

        var funds = await repository.GetActiveLinkedAsync(request.DebtId, request.LoanId, cancellationToken);

        var remaining = request.Amount;
        foreach (var fund in funds)
        {
            if (remaining == 0m)
            {
                break;
            }

            remaining -= fund.ReleaseForPayment(walletId, operationId, remaining, request.PaymentAmount ?? request.Amount);
        }

        var released = request.Amount - remaining;
        if (released > 0m)
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return released;
    }
}
