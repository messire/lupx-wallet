using FluentAssertions;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Debts.Application;
using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.Funds.Infrastructure;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Funds;

/// <summary>
/// Review v0.4.0 (docs/reviews/v0.4.0-backend-review.md): R3 — a restored payment releases a linked reserve only up
/// to what the obligation still needed; R4 — what a fund keeps released by an operation never exceeds the payment;
/// R8 — a deleted payment gives no reserve back to an archived wallet.
/// </summary>
public sealed class LinkedReserveReviewFixesTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly WalletId WalletA = new(Guid.NewGuid());
    private static readonly OperationId Payment = new(Guid.NewGuid());

    private static Fund LinkedFund() => Fund.Create(FundId.New(), "На долг", 1000m, Usd, Guid.NewGuid(), null);

    // ---- R4 ----

    [Fact]
    public void ReleaseForPayment_AfterPartialRestore_KeepsAtMostThePaymentAsReleased()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);
        fund.ReleaseForPayment(WalletA, Payment, 100m).Should().Be(100m);
        fund.RestoreLinkedRelease(Payment, WalletA, maxAmount: 50m).Should().Be(50m, "the wallet is short: half comes back");
        fund.Reserve(WalletA, Usd, 50m);

        // The payment is restored: variant A releases the full payment amount again.
        fund.ReleaseForPayment(WalletA, Payment, 100m).Should().Be(100m);

        fund.AllocatedOn(WalletA).Should().Be(0m);
        fund.ReleasedBy(Payment, WalletA).Should().Be(100m, "the payment released 100, not 150");
        fund.RestoreLinkedRelease(Payment, WalletA, maxAmount: 1000m).Should().Be(100m, "a deletion gives back only what the payment took");
    }

    [Fact]
    public async Task ReleaseLinkedReserve_WithPaymentAmount_CapsTheRecordAtThePayment()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);
        fund.ReleaseForPayment(WalletA, Payment, 80m);
        fund.RestoreLinkedRelease(Payment, WalletA, maxAmount: 30m);
        fund.ReleasedBy(Payment, WalletA).Should().Be(50m);

        var repository = Substitute.For<IFundRepository>();
        repository.GetActiveLinkedAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns([fund]);
        var handler = new ReleaseLinkedReserveCommandHandler(repository, Substitute.For<IFundsUnitOfWork>());

        var released = await handler.Handle(
            new ReleaseLinkedReserveCommand(Guid.NewGuid(), null, WalletA.Value, 40m, Payment.Value) { PaymentAmount = 80m },
            CancellationToken.None);

        released.Should().Be(40m);
        fund.ReleasedBy(Payment, WalletA).Should().Be(80m);
    }

    // ---- R8 ----

    [Fact]
    public async Task Restore_ArchivedWallet_GivesNothingBackAndKeepsTheReleaseRecord()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);
        fund.ReleaseForPayment(WalletA, Payment, 100m);
        var repository = Substitute.For<IFundRepository>();
        repository.GetActiveWithLinkedReleaseAsync(Payment, Arg.Any<CancellationToken>()).Returns([fund]);
        repository.GetReservedOnWalletAsync(WalletA, Arg.Any<CancellationToken>()).Returns(0m);
        var gateway = Substitute.For<IWalletBalanceGateway>();
        gateway.GetAsync(WalletA, Arg.Any<CancellationToken>())
            .Returns(new WalletBalanceInfo(WalletA, Usd, new Money(0m, Usd), new Money(500m, Usd), new DateOnly(2026, 1, 1), IsArchived: true));
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();

        var restored = await new RestoreLinkedReserveCommandHandler(repository, unitOfWork, gateway)
            .Handle(new RestoreLinkedReserveCommand(Payment.Value), CancellationToken.None);

        restored.Should().Be(0m);
        fund.AllocatedOn(WalletA).Should().Be(0m, "an archived wallet never holds a reserve");
        fund.ReleasedBy(Payment, WalletA).Should().Be(100m);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    // ---- R3 ----

    private static (FundsOperationRestoredHandler Handler, ISender Sender) RestoredHandler(LinkedObligationPayment? payment)
    {
        var sender = Substitute.For<ISender>();
        var lookup = Substitute.For<IDebtObligationsLookup>();
        lookup.FindPaymentByOperationAsync(Payment, Arg.Any<CancellationToken>()).Returns(payment);
        return (new FundsOperationRestoredHandler(sender, lookup), sender);
    }

    private static DomainEventNotification<OperationRestored> Restored() =>
        new(new OperationRestored(Payment, WalletA, new DateOnly(2026, 1, 1)));

    [Fact]
    public async Task OperationRestored_DebtAlreadyRepaidByAnotherPayment_ReleasesNothing()
    {
        var debtId = Guid.NewGuid();
        var (handler, sender) = RestoredHandler(
            new LinkedObligationPayment(ObligationKind.Debt, debtId, WalletA, 100m, IsObligationCancelled: false, RemainingWithoutPayment: 0m));

        await handler.Handle(Restored(), CancellationToken.None);

        await sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReleaseLinkedReserveCommand>(), default);
    }

    [Fact]
    public async Task OperationRestored_ReleasesOnlyWhatTheObligationStillNeeded()
    {
        var debtId = Guid.NewGuid();
        var (handler, sender) = RestoredHandler(
            new LinkedObligationPayment(ObligationKind.Debt, debtId, WalletA, 100m, IsObligationCancelled: false, RemainingWithoutPayment: 30m));

        await handler.Handle(Restored(), CancellationToken.None);

        await sender.Received(1).Send(
            Arg.Is<ReleaseLinkedReserveCommand>(c =>
                c.DebtId == debtId && c.LoanId == null && c.WalletId == WalletA.Value && c.Amount == 30m && c.PaymentAmount == 100m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OperationRestored_CancelledObligation_ReleasesNothing()
    {
        var (handler, sender) = RestoredHandler(
            new LinkedObligationPayment(ObligationKind.Loan, Guid.NewGuid(), WalletA, 100m, IsObligationCancelled: true, RemainingWithoutPayment: 100m));

        await handler.Handle(Restored(), CancellationToken.None);

        await sender.DidNotReceiveWithAnyArgs().Send(Arg.Any<ReleaseLinkedReserveCommand>(), default);
    }

    [Fact]
    public async Task OperationRestored_ObligationNeedsTheWholePayment_ReleasesThePaymentAmount()
    {
        var loanId = Guid.NewGuid();
        var (handler, sender) = RestoredHandler(
            new LinkedObligationPayment(ObligationKind.Loan, loanId, WalletA, 100m, IsObligationCancelled: false, RemainingWithoutPayment: 400m));

        await handler.Handle(Restored(), CancellationToken.None);

        await sender.Received(1).Send(
            Arg.Is<ReleaseLinkedReserveCommand>(c => c.LoanId == loanId && c.DebtId == null && c.Amount == 100m),
            Arg.Any<CancellationToken>());
    }
}
