using FluentAssertions;
using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Debts;

/// <summary>Debt and Creditor aggregates (ADR-0010 §3, §8).</summary>
public sealed class DebtTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static Debt NewDebt(decimal amount = 1000m) =>
        Debt.Create(DebtId.New(), CreditorId.New(), "  Долг другу ", new Money(amount, Usd), Today);

    [Fact]
    public void Create_TrimsName_RaisesCreated_IsActiveWithFullRemaining()
    {
        var debt = NewDebt();

        debt.Name.Should().Be("Долг другу");
        debt.Status(0m).Should().Be(ObligationStatus.Active);
        debt.RemainingAmount(0m).Amount.Should().Be(1000m);
        debt.DomainEvents.Should().ContainSingle(e => e is DebtCreated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveAmount_Throws(decimal amount)
    {
        var act = () => NewDebt(amount);
        act.Should().Throw<DebtsAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_BlankName_Throws()
    {
        var act = () => Debt.Create(DebtId.New(), CreditorId.New(), " ", new Money(1m, Usd), Today);
        act.Should().Throw<DebtsNameRequiredException>();
    }

    [Fact]
    public void RecordRepayment_Partial_AddsLinkAndRaisesEventWithWalletAndOperation()
    {
        var debt = NewDebt();
        debt.ClearDomainEvents();
        var walletId = WalletId.New();
        var operationId = OperationId.New();

        debt.RecordRepayment(operationId, walletId, new Money(300m, Usd), Today, countedRepaid: 0m);

        debt.Repayments.Should().ContainSingle(r => r.OperationId == operationId && r.Amount == 300m && r.WalletId == walletId);
        debt.DomainEvents.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new DebtRepaymentRecorded(debt.Id, walletId, operationId, new Money(300m, Usd)) { RepaymentDate = Today },
            o => o.Excluding(e => e.EventId).Excluding(e => e.OccurredAt));
    }

    [Fact]
    public void Status_IsRepaidWhenCountedRepaymentsCoverTheAmount_AndActiveAgainWhenTheyStopCounting()
    {
        var debt = NewDebt();

        debt.Status(1000m).Should().Be(ObligationStatus.Repaid);
        debt.RemainingAmount(1000m).Amount.Should().Be(0m);
        debt.Status(400m).Should().Be(ObligationStatus.Active);
        debt.RemainingAmount(1200m).Amount.Should().Be(0m, "remaining never goes below zero");
    }

    [Fact]
    public void RecordRepayment_MoreThanRemaining_Throws()
    {
        var debt = NewDebt();
        var act = () => debt.RecordRepayment(OperationId.New(), WalletId.New(), new Money(701m, Usd), Today, countedRepaid: 300m);
        act.Should().Throw<PaymentExceedsRemainingException>();
    }

    [Fact]
    public void RecordRepayment_ExactRemaining_Succeeds()
    {
        var debt = NewDebt();
        debt.RecordRepayment(OperationId.New(), WalletId.New(), new Money(700m, Usd), Today, countedRepaid: 300m);
        debt.Status(1000m).Should().Be(ObligationStatus.Repaid);
    }

    [Fact]
    public void RecordRepayment_WhenRepaid_ThrowsNothingToRepay()
    {
        var debt = NewDebt();
        var act = () => debt.RecordRepayment(OperationId.New(), WalletId.New(), new Money(1m, Usd), Today, countedRepaid: 1000m);
        act.Should().Throw<NothingToRepayException>();
    }

    [Fact]
    public void RecordRepayment_OtherCurrency_Throws()
    {
        var debt = NewDebt();
        var act = () => debt.RecordRepayment(OperationId.New(), WalletId.New(), new Money(1m, new CurrencyId(Guid.NewGuid())), Today, 0m);
        act.Should().Throw<PaymentCurrencyMismatchException>();
    }

    [Fact]
    public void RecordRepayment_NonPositive_Throws()
    {
        var debt = NewDebt();
        var act = () => debt.RecordRepayment(OperationId.New(), WalletId.New(), new Money(0m, Usd), Today, 0m);
        act.Should().Throw<DebtsAmountMustBePositiveException>();
    }

    [Fact]
    public void Cancelled_RejectsRepaymentsAndUpdates_AndCannotBeCancelledTwice()
    {
        var debt = NewDebt();
        debt.Cancel();

        debt.Status(0m).Should().Be(ObligationStatus.Cancelled);
        debt.Invoking(d => d.RecordRepayment(OperationId.New(), WalletId.New(), new Money(1m, Usd), Today, 0m))
            .Should().Throw<DebtNotActiveException>();
        debt.Invoking(d => d.Update(CreditorId.New(), "x", 5m, Today)).Should().Throw<DebtNotActiveException>();
        debt.Invoking(d => d.Cancel()).Should().Throw<DebtAlreadyCancelledException>();
    }

    [Fact]
    public void EnsureCanBeDeleted_WithRepayment_Throws_WithoutRepayment_Passes()
    {
        var debt = NewDebt();
        debt.Invoking(d => d.EnsureCanBeDeleted()).Should().NotThrow();

        debt.RecordRepayment(OperationId.New(), WalletId.New(), new Money(1m, Usd), Today, 0m);
        debt.Invoking(d => d.EnsureCanBeDeleted()).Should().Throw<DebtHasRepaymentsException>();
    }

    [Fact]
    public void Creditor_RenameDeactivateDelete()
    {
        var creditor = Creditor.Create(CreditorId.New(), " Банк ");
        creditor.Name.Should().Be("Банк");
        creditor.IsActive.Should().BeTrue();

        creditor.ClearDomainEvents();
        creditor.Rename("Банк");
        creditor.DomainEvents.Should().BeEmpty("renaming to the same name is a no-op");

        creditor.Rename("Другой банк");
        creditor.Deactivate();
        creditor.IsActive.Should().BeFalse();
        creditor.DomainEvents.Should().HaveCount(2);

        creditor.Invoking(c => c.EnsureCanBeDeleted(isUsed: true)).Should().Throw<CreditorInUseException>();
        creditor.Invoking(c => c.EnsureCanBeDeleted(isUsed: false)).Should().NotThrow();
    }
}
