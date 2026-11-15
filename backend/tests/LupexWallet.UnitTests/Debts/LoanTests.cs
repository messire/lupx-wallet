using FluentAssertions;
using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Debts;

/// <summary>Loan aggregate and its schedule rules (ADR-0010 §3, issue #8).</summary>
public sealed class LoanTests
{
    private static readonly CurrencyId Eur = new(Guid.NewGuid());
    private static readonly DateOnly Today = new(2026, 11, 14);

    private static Loan NewLoan(decimal amount = 1200m) =>
        Loan.Create(LoanId.New(), CreditorId.New(), "Автокредит", new Money(amount, Eur), new DateOnly(2026, 1, 1));

    private static LoanScheduleEntry Item(int month, int day, decimal amount) => new(new DateOnly(2026, month, day), amount);

    [Fact]
    public void Evaluate_PaymentsCoverEarliestItemsFirst_StatusesDerived()
    {
        IReadOnlyList<LoanScheduleEntry> schedule = [Item(10, 1, 100m), Item(11, 1, 100m), Item(11, 14, 100m), Item(12, 1, 100m), Item(12, 15, 100m)];

        var states = LoanScheduleCalculator.Evaluate(schedule, countedPaid: 150m, Today);

        states.Select(s => s.Status).Should().Equal(
            LoanScheduleItemStatus.Paid,
            LoanScheduleItemStatus.Overdue,   // 50 of 100 paid, date in the past: overdue wins over partial
            LoanScheduleItemStatus.Expected,  // due today, nothing paid
            LoanScheduleItemStatus.Expected,
            LoanScheduleItemStatus.Expected);
        states[1].PaidAmount.Should().Be(50m);
        states[1].OutstandingAmount.Should().Be(50m);

        LoanScheduleCalculator.NextPayment(states)!.DueDate.Should().Be(new DateOnly(2026, 11, 1));
    }

    [Fact]
    public void Evaluate_PartialOnFutureItem_IsPartial()
    {
        var states = LoanScheduleCalculator.Evaluate([Item(12, 1, 100m)], countedPaid: 40m, Today);
        states.Single().Status.Should().Be(LoanScheduleItemStatus.Partial);
    }

    [Fact]
    public void Evaluate_AllPaid_NoNextPayment()
    {
        var states = LoanScheduleCalculator.Evaluate([Item(10, 1, 100m), Item(12, 1, 100m)], countedPaid: 250m, Today);
        states.Should().OnlyContain(s => s.Status == LoanScheduleItemStatus.Paid);
        LoanScheduleCalculator.NextPayment(states).Should().BeNull();
    }

    [Fact]
    public void GenerateEqualMonthly_RemainderGoesToLastPayment_ClampsDayOfMonth()
    {
        var entries = LoanScheduleCalculator.GenerateEqualMonthly(1000m, new DateOnly(2026, 1, 31), 3);

        entries.Select(e => e.Amount).Should().Equal(333.33m, 333.33m, 333.34m);
        entries.Sum(e => e.Amount).Should().Be(1000m);
        entries.Select(e => e.DueDate).Should().Equal(new DateOnly(2026, 1, 31), new DateOnly(2026, 2, 28), new DateOnly(2026, 3, 31));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(601)]
    public void GenerateEqualMonthly_CountOutOfRange_Throws(int count)
    {
        var act = () => LoanScheduleCalculator.GenerateEqualMonthly(1000m, Today, count);
        act.Should().Throw<LoanScheduleGenerationCountOutOfRangeException>();
    }

    [Fact]
    public void GenerateSchedule_ReplacesItems_RaisesScheduleChangedWithBothVersions()
    {
        var loan = NewLoan();
        loan.ReplaceSchedule([Item(3, 1, 1200m)]);
        loan.ClearDomainEvents();

        loan.GenerateSchedule(new DateOnly(2026, 4, 10), 12);

        loan.ScheduleItems.Should().HaveCount(12);
        loan.OrderedSchedule().Sum(x => x.Amount).Should().Be(1200m);
        var changed = loan.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<LoanScheduleChanged>().Subject;
        changed.PreviousSchedule.Should().ContainSingle();
        changed.NewSchedule.Should().HaveCount(12);
    }

    [Fact]
    public void ReplaceSchedule_KeepsEnteredOrderForSameDate_AndRejectsNonPositive()
    {
        var loan = NewLoan();
        loan.ReplaceSchedule([Item(5, 1, 20m), Item(4, 1, 10m), Item(5, 1, 30m)]);

        loan.OrderedSchedule().Select(x => x.Amount).Should().Equal(10m, 20m, 30m);
        loan.Invoking(l => l.ReplaceSchedule([Item(5, 1, 0m)])).Should().Throw<LoanScheduleItemAmountMustBePositiveException>();
    }

    [Fact]
    public void RecordPayment_ValidatesRemainingCurrencyAndStatus()
    {
        var loan = NewLoan(500m);
        var walletId = WalletId.New();

        loan.RecordPayment(OperationId.New(), walletId, new Money(200m, Eur), Today, countedPaid: 0m);
        loan.DomainEvents.OfType<LoanPaymentRecorded>().Should().ContainSingle(e => e.WalletId == walletId && e.Amount.Amount == 200m);

        loan.Invoking(l => l.RecordPayment(OperationId.New(), walletId, new Money(301m, Eur), Today, 200m))
            .Should().Throw<PaymentExceedsRemainingException>();
        loan.Invoking(l => l.RecordPayment(OperationId.New(), walletId, new Money(1m, new CurrencyId(Guid.NewGuid())), Today, 200m))
            .Should().Throw<PaymentCurrencyMismatchException>();

        loan.Status(500m).Should().Be(ObligationStatus.Repaid);
        loan.Invoking(l => l.EnsureCanBeDeleted()).Should().Throw<LoanHasPaymentsException>();

        loan.Cancel();
        loan.Invoking(l => l.ReplaceSchedule([])).Should().Throw<LoanNotActiveException>();
        loan.Invoking(l => l.RecordPayment(OperationId.New(), walletId, new Money(1m, Eur), Today, 0m)).Should().Throw<LoanNotActiveException>();
        loan.Status(0m).Should().Be(ObligationStatus.Cancelled);
    }
}
