using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Debts.Application;
using LupexWallet.Debts.Domain;
using LupexWallet.Funds.Application;
using LupexWallet.Operations.Domain;
using MediatR;

namespace LupexWallet.Funds.Infrastructure;

/// <summary>
/// ADR-0010 §4, B9 (ADR-0001 §2 way e): a debt repayment releases the reserve of the funds linked to
/// the debt on the paying wallet, in the same transaction as the payment. Registered in AddFundsModule.
/// </summary>
public sealed class FundsDebtRepaymentRecordedHandler(ISender sender)
    : INotificationHandler<DomainEventNotification<DebtRepaymentRecorded>>
{
    public Task Handle(DomainEventNotification<DebtRepaymentRecorded> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return sender.Send(
            new ReleaseLinkedReserveCommand(e.DebtId.Value, null, e.WalletId.Value, e.Amount.Amount, e.OperationId.Value),
            cancellationToken);
    }
}

/// <summary>Same as <see cref="FundsDebtRepaymentRecordedHandler"/> for a loan payment.</summary>
public sealed class FundsLoanPaymentRecordedHandler(ISender sender)
    : INotificationHandler<DomainEventNotification<LoanPaymentRecorded>>
{
    public Task Handle(DomainEventNotification<LoanPaymentRecorded> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return sender.Send(
            new ReleaseLinkedReserveCommand(null, e.LoanId.Value, e.WalletId.Value, e.Amount.Amount, e.OperationId.Value),
            cancellationToken);
    }
}

/// <summary>
/// The payment expense was soft-deleted: the reserve it released comes back within the wallet's
/// free funds. A no-op for operations that released nothing.
/// </summary>
public sealed class FundsOperationDeletedHandler(ISender sender)
    : INotificationHandler<DomainEventNotification<OperationDeleted>>
{
    public Task Handle(DomainEventNotification<OperationDeleted> notification, CancellationToken cancellationToken) =>
        sender.Send(new RestoreLinkedReserveCommand(notification.DomainEvent.OperationId.Value), cancellationToken);
}

/// <summary>
/// The payment expense was restored: it releases the linked reserve again, like a new payment of the
/// recorded amount from the operation's current wallet. A no-op for operations that pay no debt/loan.
/// Review v0.4.0, R3: the release is not more than what the obligation still needed before the restore
/// (its amount minus the other counted payments), and nothing is released for a cancelled or already
/// repaid obligation — a restore never takes a reserve for a debt there is nothing left to pay on.
/// </summary>
public sealed class FundsOperationRestoredHandler(ISender sender, IDebtObligationsLookup obligations)
    : INotificationHandler<DomainEventNotification<OperationRestored>>
{
    public async Task Handle(DomainEventNotification<OperationRestored> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var payment = await obligations.FindPaymentByOperationAsync(e.OperationId, cancellationToken);
        if (payment is null)
        {
            return;
        }

        var amount = payment.IsObligationCancelled ? 0m : Math.Min(payment.Amount, payment.RemainingWithoutPayment);
        if (amount <= 0m)
        {
            return;
        }

        var isDebt = payment.Kind == ObligationKind.Debt;
        await sender.Send(
            new ReleaseLinkedReserveCommand(
                isDebt ? payment.ObligationId : null,
                isDebt ? null : payment.ObligationId,
                e.WalletId.Value,
                amount,
                e.OperationId.Value)
            {
                PaymentAmount = payment.Amount,
            },
            cancellationToken);
    }
}
