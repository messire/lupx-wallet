using FluentAssertions;
using LupexWallet.Debts.Application;
using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Funds;

/// <summary>
/// ADR-0010 §4, B9: a linked payment releases the reserve and remembers it per operation; deleting
/// the expense restores it within the wallet's free funds; the fund link is validated against Debts.
/// </summary>
public sealed class LinkedReleaseTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Eur = new(Guid.NewGuid());
    private static readonly WalletId WalletA = new(Guid.NewGuid());
    private static readonly OperationId Payment = new(Guid.NewGuid());

    private static Fund LinkedFund(Guid? debtId = null) => Fund.Create(FundId.New(), "На долг", 1000m, Usd, debtId ?? Guid.NewGuid(), null);

    // ---- Domain ----

    [Fact]
    public void ReleaseForPayment_RemembersTheReleasedAmountPerOperation()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);

        var released = fund.ReleaseForPayment(WalletA, Payment, 30m);

        released.Should().Be(30m);
        fund.AllocatedOn(WalletA).Should().Be(70m);
        fund.ReleasedBy(Payment, WalletA).Should().Be(30m);
        fund.DomainEvents.OfType<FundReleased>().Last().Should().Match<FundReleased>(e =>
            e.Reason == FundReleaseReason.LinkedPayment && e.OperationId == Payment);
    }

    [Fact]
    public void ReleaseForPayment_NoAllocation_ReleasesNothingAndRemembersNothing()
    {
        var fund = LinkedFund();

        fund.ReleaseForPayment(WalletA, Payment, 30m).Should().Be(0m);
        fund.LinkedReleases.Should().BeEmpty();
    }

    [Fact]
    public void RestoreLinkedRelease_GivesBackUpToTheReleasedAmountAndTheLimit()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);
        fund.ReleaseForPayment(WalletA, Payment, 60m);

        var restored = fund.RestoreLinkedRelease(Payment, WalletA, maxAmount: 45m);

        restored.Should().Be(45m);
        fund.AllocatedOn(WalletA).Should().Be(85m);
        fund.ReleasedBy(Payment, WalletA).Should().Be(15m, "the part that did not fit stays released");
        fund.DomainEvents.OfType<FundReserved>().Last().Should().Match<FundReserved>(e =>
            e.Reason == FundReserveReason.LinkedPaymentDeleted && e.OperationId == Payment && e.Amount == 45m);
    }

    [Fact]
    public void RestoreLinkedRelease_ClosedFund_RestoresNothing()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);
        fund.ReleaseForPayment(WalletA, Payment, 60m);
        fund.Close();

        fund.RestoreLinkedRelease(Payment, WalletA, 1000m).Should().Be(0m);
    }

    [Fact]
    public void ReleaseAgainAfterRestore_AccumulatesOnTheSameRecord()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 100m);
        fund.ReleaseForPayment(WalletA, Payment, 60m);
        fund.RestoreLinkedRelease(Payment, WalletA, 1000m);

        fund.ReleaseForPayment(WalletA, Payment, 60m);

        fund.LinkedReleases.Should().ContainSingle().Which.Amount.Should().Be(60m);
        fund.AllocatedOn(WalletA).Should().Be(40m);
    }

    // ---- RestoreLinkedReserveCommand ----

    private static (RestoreLinkedReserveCommandHandler Handler, IFundsUnitOfWork UnitOfWork) RestoreHandler(
        decimal balance, decimal reservedOnWallet, params Fund[] funds)
    {
        var repository = Substitute.For<IFundRepository>();
        repository.GetActiveWithLinkedReleaseAsync(Payment, Arg.Any<CancellationToken>()).Returns(funds);
        repository.GetReservedOnWalletAsync(WalletA, Arg.Any<CancellationToken>()).Returns(reservedOnWallet);
        var gateway = Substitute.For<IWalletBalanceGateway>();
        gateway.GetAsync(WalletA, Arg.Any<CancellationToken>())
            .Returns(new WalletBalanceInfo(WalletA, Usd, new Money(0m, Usd), new Money(balance, Usd), new DateOnly(2026, 1, 1), false));
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();
        return (new RestoreLinkedReserveCommandHandler(repository, unitOfWork, gateway), unitOfWork);
    }

    [Fact]
    public async Task Restore_EnoughFreeFunds_RestoresEverythingOldestFirst()
    {
        var first = LinkedFund();
        first.Reserve(WalletA, Usd, 10m);
        first.ReleaseForPayment(WalletA, Payment, 10m);
        var second = LinkedFund();
        second.Reserve(WalletA, Usd, 20m);
        second.ReleaseForPayment(WalletA, Payment, 5m);
        // Balance 100 after the deletion; still reserved: 0 + 15 → free 85.
        var (handler, unitOfWork) = RestoreHandler(balance: 100m, reservedOnWallet: 15m, first, second);

        var restored = await handler.Handle(new RestoreLinkedReserveCommand(Payment.Value), CancellationToken.None);

        restored.Should().Be(15m);
        first.AllocatedOn(WalletA).Should().Be(10m);
        second.AllocatedOn(WalletA).Should().Be(20m);
        await unitOfWork.Received(1).AcquireWalletLockAsync(WalletA, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_NotEnoughFreeFunds_RestoresWhatFitsOldestFirst()
    {
        var first = LinkedFund();
        first.Reserve(WalletA, Usd, 10m);
        first.ReleaseForPayment(WalletA, Payment, 10m);
        var second = LinkedFund();
        second.Reserve(WalletA, Usd, 10m);
        second.ReleaseForPayment(WalletA, Payment, 10m);
        // Free = 12 − 0 = 12: the oldest gets 10, the next 2.
        var (handler, _) = RestoreHandler(balance: 12m, reservedOnWallet: 0m, first, second);

        var restored = await handler.Handle(new RestoreLinkedReserveCommand(Payment.Value), CancellationToken.None);

        restored.Should().Be(12m);
        first.AllocatedOn(WalletA).Should().Be(10m);
        second.AllocatedOn(WalletA).Should().Be(2m);
        second.ReleasedBy(Payment, WalletA).Should().Be(8m);
    }

    [Fact]
    public async Task Restore_NoFreeFunds_RestoresNothingAndDoesNotSave()
    {
        var fund = LinkedFund();
        fund.Reserve(WalletA, Usd, 50m);
        fund.ReleaseForPayment(WalletA, Payment, 20m);
        var (handler, unitOfWork) = RestoreHandler(balance: 30m, reservedOnWallet: 30m, fund);

        (await handler.Handle(new RestoreLinkedReserveCommand(Payment.Value), CancellationToken.None)).Should().Be(0m);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Restore_OperationReleasedNothing_IsANoOp()
    {
        var (handler, unitOfWork) = RestoreHandler(balance: 30m, reservedOnWallet: 0m);

        (await handler.Handle(new RestoreLinkedReserveCommand(Payment.Value), CancellationToken.None)).Should().Be(0m);
        await unitOfWork.DidNotReceive().AcquireWalletLockAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>());
    }

    // ---- FundLinkValidator ----

    private static FundLinkValidator Validator(ObligationReference? debt = null, ObligationReference? loan = null)
    {
        var lookup = Substitute.For<IDebtObligationsLookup>();
        lookup.FindDebtAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(debt);
        lookup.FindLoanAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(loan);
        return new FundLinkValidator(lookup);
    }

    [Fact]
    public async Task Validator_UnknownDebt_Throws()
    {
        var act = () => Validator().EnsureValidAsync(Guid.NewGuid(), null, Usd, isNewLink: true, CancellationToken.None);
        await act.Should().ThrowAsync<FundLinkedDebtNotFoundException>();
    }

    [Fact]
    public async Task Validator_UnknownLoan_Throws()
    {
        var act = () => Validator().EnsureValidAsync(null, Guid.NewGuid(), Usd, isNewLink: true, CancellationToken.None);
        await act.Should().ThrowAsync<FundLinkedLoanNotFoundException>();
    }

    [Fact]
    public async Task Validator_OtherCurrency_Throws()
    {
        var id = Guid.NewGuid();
        var act = () => Validator(debt: new ObligationReference(ObligationKind.Debt, id, Eur, false))
            .EnsureValidAsync(id, null, Usd, isNewLink: true, CancellationToken.None);
        await act.Should().ThrowAsync<FundLinkCurrencyMismatchException>();
    }

    [Fact]
    public async Task Validator_CancelledLoan_NewLinkThrows_ExistingLinkPasses()
    {
        var id = Guid.NewGuid();
        var validator = Validator(loan: new ObligationReference(ObligationKind.Loan, id, Usd, IsCancelled: true));

        await validator.Invoking(v => v.EnsureValidAsync(null, id, Usd, isNewLink: true, CancellationToken.None))
            .Should().ThrowAsync<FundLinkedObligationCancelledException>();
        await validator.Invoking(v => v.EnsureValidAsync(null, id, Usd, isNewLink: false, CancellationToken.None))
            .Should().NotThrowAsync();
    }

    [Fact]
    public async Task Validator_NoLink_DoesNotCallDebts()
    {
        var lookup = Substitute.For<IDebtObligationsLookup>();

        await new FundLinkValidator(lookup).EnsureValidAsync(null, null, Usd, isNewLink: true, CancellationToken.None);

        await lookup.DidNotReceiveWithAnyArgs().FindDebtAsync(default, default);
        await lookup.DidNotReceiveWithAnyArgs().FindLoanAsync(default, default);
    }
}
