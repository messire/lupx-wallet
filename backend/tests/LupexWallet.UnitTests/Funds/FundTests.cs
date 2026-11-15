using FluentAssertions;
using LupexWallet.Funds.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Funds;

/// <summary>Fund aggregate invariants (ADR-0010 §4, §8). The free-funds check lives in ReservationPolicy.</summary>
public sealed class FundTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly WalletId WalletA = new(Guid.NewGuid());
    private static readonly WalletId WalletB = new(Guid.NewGuid());

    private static Fund CreateFund(decimal target = 1000m, Guid? debtId = null, Guid? loanId = null) =>
        Fund.Create(FundId.New(), "Отпуск", target, Usd, debtId, loanId);

    [Fact]
    public void Create_ValidFund_IsActiveWithoutReserve()
    {
        var fund = CreateFund();

        fund.Status.Should().Be(FundStatus.Active);
        fund.Reserved.Amount.Should().Be(0m);
        fund.HasAllocationHistory.Should().BeFalse();
        fund.DomainEvents.Should().ContainSingle(e => e is FundCreated);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_Throws(string name)
    {
        var act = () => Fund.Create(FundId.New(), name, 1m, Usd, null, null);

        act.Should().Throw<FundNameRequiredException>();
    }

    [Fact]
    public void Create_TooLongName_Throws()
    {
        var act = () => Fund.Create(FundId.New(), new string('x', Fund.MaxNameLength + 1), 1m, Usd, null, null);

        act.Should().Throw<FundNameTooLongException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_NonPositiveTarget_Throws(decimal target)
    {
        var act = () => CreateFund(target);

        act.Should().Throw<FundTargetAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_LinkedToDebtAndLoan_Throws()
    {
        var act = () => CreateFund(debtId: Guid.NewGuid(), loanId: Guid.NewGuid());

        act.Should().Throw<FundLinkAmbiguousException>();
    }

    [Fact]
    public void Reserve_SeveralWallets_SumsAllocations()
    {
        var fund = CreateFund();

        fund.Reserve(WalletA, Usd, 100m);
        fund.Reserve(WalletA, Usd, 50.5m);
        fund.Reserve(WalletB, Usd, 0.000001m);

        fund.AllocatedOn(WalletA).Should().Be(150.5m);
        fund.AllocatedOn(WalletB).Should().Be(0.000001m);
        fund.Reserved.Amount.Should().Be(150.500001m);
        fund.Allocations.Should().HaveCount(2, "a second reserve on the same wallet increases its allocation");
        fund.DomainEvents.OfType<FundReserved>().Last().Should()
            .Match<FundReserved>(e => e.WalletId == WalletB && e.OldAllocation == 0m && e.NewAllocation == 0.000001m);
    }

    [Fact]
    public void Reserve_WalletInAnotherCurrency_Throws()
    {
        var fund = CreateFund();

        var act = () => fund.Reserve(WalletA, new CurrencyId(Guid.NewGuid()), 10m);

        act.Should().Throw<FundWalletCurrencyMismatchException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Reserve_NonPositiveAmount_Throws(decimal amount)
    {
        var fund = CreateFund();

        var act = () => fund.Reserve(WalletA, Usd, amount);

        act.Should().Throw<FundAmountMustBePositiveException>();
    }

    [Fact]
    public void Release_PartOfAllocation_DecreasesIt()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 100m);

        fund.Release(WalletA, 40m);

        fund.AllocatedOn(WalletA).Should().Be(60m);
        fund.DomainEvents.OfType<FundReleased>().Single().Reason.Should().Be(FundReleaseReason.Manual);
    }

    [Fact]
    public void Release_MoreThanAllocation_Throws()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 100m);

        var act = () => fund.Release(WalletA, 100.01m);

        act.Should().Throw<FundReleaseExceedsAllocationException>().Which.Allocated.Should().Be(100m);
    }

    [Fact]
    public void Release_WalletWithoutAllocation_Throws()
    {
        var fund = CreateFund();

        var act = () => fund.Release(WalletA, 1m);

        act.Should().Throw<FundReleaseExceedsAllocationException>();
    }

    [Fact]
    public void Release_WholeAllocation_KeepsTheHistory()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 100m);

        fund.Release(WalletA, 100m);

        fund.Reserved.Amount.Should().Be(0m);
        fund.HasAllocationHistory.Should().BeTrue();
        var act = () => fund.EnsureCanBeDeleted();
        act.Should().Throw<FundDeletionNotAllowedException>();
    }

    [Fact]
    public void ReleaseUpTo_CapsAtTheAllocation()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 30m);

        var released = fund.ReleaseUpTo(WalletA, 50m);

        released.Should().Be(30m);
        fund.AllocatedOn(WalletA).Should().Be(0m);
        fund.DomainEvents.OfType<FundReleased>().Single().Reason.Should().Be(FundReleaseReason.LinkedPayment);
    }

    [Fact]
    public void ReleaseUpTo_LessThanAllocation_ReleasesTheAmount()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 30m);

        fund.ReleaseUpTo(WalletA, 12m).Should().Be(12m);
        fund.AllocatedOn(WalletA).Should().Be(18m);
    }

    [Fact]
    public void ReleaseUpTo_OtherWalletOrClosedFund_ReleasesNothing()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 30m);

        fund.ReleaseUpTo(WalletB, 10m).Should().Be(0m);

        fund.Close();
        fund.ReleaseUpTo(WalletA, 10m).Should().Be(0m);
    }

    [Fact]
    public void Close_ReleasesAllAllocations()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 30m);
        fund.Reserve(WalletB, Usd, 70m);
        fund.ClearDomainEvents();

        fund.Close();

        fund.Status.Should().Be(FundStatus.Closed);
        fund.ClosedAt.Should().NotBeNull();
        fund.Reserved.Amount.Should().Be(0m);
        fund.DomainEvents.OfType<FundReleased>().Should().HaveCount(2)
            .And.OnlyContain(e => e.Reason == FundReleaseReason.FundClosed && e.NewAllocation == 0m);
        fund.DomainEvents.Should().ContainSingle(e => e is FundClosed);
    }

    [Fact]
    public void ClosedFund_RejectsChanges()
    {
        var fund = CreateFund();
        fund.Close();

        fund.Invoking(f => f.Reserve(WalletA, Usd, 1m)).Should().Throw<FundClosedException>();
        fund.Invoking(f => f.Release(WalletA, 1m)).Should().Throw<FundClosedException>();
        fund.Invoking(f => f.Update("Новое", 5m, Usd, null, null)).Should().Throw<FundClosedException>();
        fund.Invoking(f => f.Close()).Should().Throw<FundClosedException>();
    }

    [Fact]
    public void Update_CurrencyBeforeFirstReserve_IsAllowed()
    {
        var fund = CreateFund();
        var eur = new CurrencyId(Guid.NewGuid());

        fund.Update(" Машина ", 5000m, eur, Guid.NewGuid(), null);

        fund.Name.Should().Be("Машина");
        fund.CurrencyId.Should().Be(eur);
        fund.TargetAmount.Should().Be(5000m);
        fund.LinkedDebtId.Should().NotBeNull();
    }

    [Fact]
    public void Update_CurrencyAfterReserve_EvenReleased_Throws()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 1m);
        fund.Release(WalletA, 1m);

        var act = () => fund.Update("Отпуск", 1000m, new CurrencyId(Guid.NewGuid()), null, null);

        act.Should().Throw<FundCurrencyChangeNotAllowedException>();
    }

    [Fact]
    public void Update_SameCurrencyAfterReserve_IsAllowed()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 1m);

        fund.Update("Отпуск 2", 2000m, Usd, null, null);

        fund.TargetAmount.Should().Be(2000m);
    }

    [Fact]
    public void EnsureCanBeDeleted_WithoutAllocations_Passes()
    {
        var fund = CreateFund();

        fund.Invoking(f => f.EnsureCanBeDeleted()).Should().NotThrow();
    }
}
