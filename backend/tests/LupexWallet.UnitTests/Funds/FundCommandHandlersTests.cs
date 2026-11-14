using FluentAssertions;
using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Funds;

/// <summary>Funds command handlers: routing, lock order, linked-payment release (ADR-0010 §4, B9) and views with deficits.</summary>
public sealed class FundCommandHandlersTests
{
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly WalletId WalletA = new(Guid.NewGuid());
    private static readonly WalletId WalletB = new(Guid.NewGuid());

    private static Fund CreateFund(Guid? debtId = null, Guid? loanId = null) =>
        Fund.Create(FundId.New(), "Фонд", 1000m, Usd, debtId, loanId);

    private static IFundRepository RepositoryReturning(Fund? fund)
    {
        var repository = Substitute.For<IFundRepository>();
        repository.GetByIdAsync(Arg.Any<FundId>(), Arg.Any<CancellationToken>()).Returns(fund);
        repository.GetReservedByWalletAsync(Arg.Any<CancellationToken>()).Returns([]);
        return repository;
    }

    private static IWalletTotalsSource Wallets(params (WalletId Id, decimal Balance)[] wallets)
    {
        var source = Substitute.For<IWalletTotalsSource>();
        source.GetAllAsync(Arg.Any<CancellationToken>())
            .Returns(wallets.Select(w => new WalletTotalInfo(w.Id, Usd, w.Balance, true, false)).ToList());
        return source;
    }

    private static IWalletBalanceGateway Gateway(WalletId walletId, decimal balance)
    {
        var gateway = Substitute.For<IWalletBalanceGateway>();
        gateway.GetAsync(walletId, Arg.Any<CancellationToken>())
            .Returns(new WalletBalanceInfo(walletId, Usd, new Money(0m, Usd), new Money(balance, Usd), new DateOnly(2026, 1, 1), false));
        return gateway;
    }

    // ---- Create / Update / Delete / Close ----

    [Fact]
    public async Task CreateFund_InactiveCurrency_Throws()
    {
        var repository = RepositoryReturning(null);
        var currencyLookup = Substitute.For<ICurrencyLookup>();
        var handler = new CreateFundCommandHandler(
            repository, Substitute.For<IFundsUnitOfWork>(), currencyLookup, new FundViewBuilder(repository, Wallets()));

        var act = () => handler.Handle(new CreateFundCommand("Фонд", 10m, Guid.NewGuid(), null, null), CancellationToken.None);

        await act.Should().ThrowAsync<FundCurrencyNotFoundException>();
        repository.DidNotReceive().Add(Arg.Any<Fund>());
    }

    [Fact]
    public async Task CreateFund_Valid_AddsAndSaves()
    {
        var repository = RepositoryReturning(null);
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();
        var currencyLookup = Substitute.For<ICurrencyLookup>();
        currencyLookup.GetAsync(Usd, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Usd, "USD"));
        var handler = new CreateFundCommandHandler(repository, unitOfWork, currencyLookup, new FundViewBuilder(repository, Wallets()));

        var dto = await handler.Handle(new CreateFundCommand("Фонд", 200m, Usd.Value, null, null), CancellationToken.None);

        dto.TargetAmount.Should().Be(200m);
        dto.Progress.Should().Be(0m);
        repository.Received(1).Add(Arg.Any<Fund>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task UpdateFund_UnknownFund_Throws()
    {
        var repository = RepositoryReturning(null);
        var handler = new UpdateFundCommandHandler(
            repository, Substitute.For<IFundsUnitOfWork>(), Substitute.For<ICurrencyLookup>(), new FundViewBuilder(repository, Wallets()));

        var act = () => handler.Handle(new UpdateFundCommand(Guid.NewGuid(), "x", 1m, Usd.Value, null, null), CancellationToken.None);

        await act.Should().ThrowAsync<FundNotFoundException>();
    }

    [Fact]
    public async Task DeleteFund_WithAllocationHistory_Throws()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 5m);
        var repository = RepositoryReturning(fund);
        var handler = new DeleteFundCommandHandler(repository, Substitute.For<IFundsUnitOfWork>());

        var act = () => handler.Handle(new DeleteFundCommand(fund.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<FundDeletionNotAllowedException>();
        repository.DidNotReceive().Remove(Arg.Any<Fund>());
    }

    [Fact]
    public async Task DeleteFund_WithoutHistory_RemovesAndRaisesDeleted()
    {
        var fund = CreateFund();
        var repository = RepositoryReturning(fund);
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();
        var handler = new DeleteFundCommandHandler(repository, unitOfWork);

        await handler.Handle(new DeleteFundCommand(fund.Id.Value), CancellationToken.None);

        repository.Received(1).Remove(fund);
        fund.DomainEvents.Should().Contain(e => e is FundDeleted);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseFund_ReleasesEverything()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 5m);
        var repository = RepositoryReturning(fund);
        var handler = new CloseFundCommandHandler(repository, Substitute.For<IFundsUnitOfWork>(), new FundViewBuilder(repository, Wallets((WalletA, 0m))));

        var dto = await handler.Handle(new CloseFundCommand(fund.Id.Value), CancellationToken.None);

        dto.Status.Should().Be(FundStatus.Closed);
        dto.ReservedAmount.Should().Be(0m);
        dto.Allocations.Should().BeEmpty();
        dto.HasAllocationHistory.Should().BeTrue();
    }

    // ---- Reserve / Release ----

    [Fact]
    public async Task ReserveFund_TakesTheWalletLockBeforeReadingAndSaves()
    {
        var fund = CreateFund();
        var calls = new List<string>();
        var repository = RepositoryReturning(fund);
        repository.When(r => r.GetByIdAsync(Arg.Any<FundId>(), Arg.Any<CancellationToken>())).Do(_ => calls.Add("read-fund"));
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();
        unitOfWork.When(u => u.AcquireWalletLockAsync(WalletA, Arg.Any<CancellationToken>())).Do(_ => calls.Add("lock"));
        unitOfWork.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => calls.Add("save"));
        var handler = new ReserveFundCommandHandler(
            repository, unitOfWork, new ReservationPolicy(repository, Gateway(WalletA, 100m)), new FundViewBuilder(repository, Wallets((WalletA, 100m))));

        var dto = await handler.Handle(new ReserveFundCommand(fund.Id.Value, WalletA.Value, 40m), CancellationToken.None);

        calls.Should().Equal("lock", "read-fund", "save");
        dto.ReservedAmount.Should().Be(40m);
    }

    [Fact]
    public async Task ReserveFund_UnknownFund_Throws()
    {
        var repository = RepositoryReturning(null);
        var handler = new ReserveFundCommandHandler(
            repository, Substitute.For<IFundsUnitOfWork>(), new ReservationPolicy(repository, Gateway(WalletA, 100m)), new FundViewBuilder(repository, Wallets()));

        var act = () => handler.Handle(new ReserveFundCommand(Guid.NewGuid(), WalletA.Value, 1m), CancellationToken.None);

        await act.Should().ThrowAsync<FundNotFoundException>();
    }

    [Fact]
    public async Task ReleaseFund_DecreasesTheAllocation()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 40m);
        var repository = RepositoryReturning(fund);
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();
        var handler = new ReleaseFundCommandHandler(repository, unitOfWork, new FundViewBuilder(repository, Wallets((WalletA, 100m))));

        var dto = await handler.Handle(new ReleaseFundCommand(fund.Id.Value, WalletA.Value, 15m), CancellationToken.None);

        dto.ReservedAmount.Should().Be(25m);
        await unitOfWork.Received(1).AcquireWalletLockAsync(WalletA, Arg.Any<CancellationToken>());
    }

    // ---- Linked debt/loan payment (ADR-0010 §4, B9) ----

    private static (ReleaseLinkedReserveCommandHandler Handler, IFundsUnitOfWork UnitOfWork) LinkedHandler(params Fund[] funds)
    {
        var repository = Substitute.For<IFundRepository>();
        repository.GetActiveLinkedAsync(Arg.Any<Guid?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>()).Returns(funds);
        var unitOfWork = Substitute.For<IFundsUnitOfWork>();
        return (new ReleaseLinkedReserveCommandHandler(repository, unitOfWork), unitOfWork);
    }

    [Fact]
    public async Task ReleaseLinked_PaymentBelowAllocation_ReleasesThePayment()
    {
        var debtId = Guid.NewGuid();
        var fund = CreateFund(debtId: debtId);
        fund.Reserve(WalletA, Usd, 100m);
        var (handler, unitOfWork) = LinkedHandler(fund);

        var released = await handler.Handle(new ReleaseLinkedReserveCommand(debtId, null, WalletA.Value, 30m), CancellationToken.None);

        released.Should().Be(30m);
        fund.AllocatedOn(WalletA).Should().Be(70m);
        await unitOfWork.Received(1).AcquireWalletLockAsync(WalletA, Arg.Any<CancellationToken>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReleaseLinked_PaymentAboveAllocation_ReleasesOnlyTheAllocation()
    {
        var loanId = Guid.NewGuid();
        var fund = CreateFund(loanId: loanId);
        fund.Reserve(WalletA, Usd, 20m);
        fund.Reserve(WalletB, Usd, 50m);
        var (handler, _) = LinkedHandler(fund);

        var released = await handler.Handle(new ReleaseLinkedReserveCommand(null, loanId, WalletA.Value, 30m), CancellationToken.None);

        released.Should().Be(20m);
        fund.AllocatedOn(WalletA).Should().Be(0m);
        fund.AllocatedOn(WalletB).Should().Be(50m, "only the paying wallet's reserve is released");
    }

    [Fact]
    public async Task ReleaseLinked_SeveralFunds_ReleasesOldestFirstWithinThePayment()
    {
        var debtId = Guid.NewGuid();
        var first = CreateFund(debtId: debtId);
        first.Reserve(WalletA, Usd, 10m);
        var second = CreateFund(debtId: debtId);
        second.Reserve(WalletA, Usd, 10m);
        var (handler, _) = LinkedHandler(first, second);

        var released = await handler.Handle(new ReleaseLinkedReserveCommand(debtId, null, WalletA.Value, 15m), CancellationToken.None);

        released.Should().Be(15m);
        first.AllocatedOn(WalletA).Should().Be(0m);
        second.AllocatedOn(WalletA).Should().Be(5m);
    }

    [Fact]
    public async Task ReleaseLinked_NoReserveOnTheWallet_ReleasesNothingAndDoesNotSave()
    {
        var debtId = Guid.NewGuid();
        var fund = CreateFund(debtId: debtId);
        fund.Reserve(WalletB, Usd, 10m);
        var (handler, unitOfWork) = LinkedHandler(fund);

        var released = await handler.Handle(new ReleaseLinkedReserveCommand(debtId, null, WalletA.Value, 15m), CancellationToken.None);

        released.Should().Be(0m);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ReleaseLinked_RequiresExactlyOneLink(bool withDebt, bool withLoan)
    {
        var (handler, _) = LinkedHandler();

        var act = () => handler.Handle(
            new ReleaseLinkedReserveCommand(withDebt ? Guid.NewGuid() : null, withLoan ? Guid.NewGuid() : null, WalletA.Value, 1m),
            CancellationToken.None);

        await act.Should().ThrowAsync<FundLinkAmbiguousException>();
    }

    // ---- Views: progress and deficit ----

    [Fact]
    public async Task View_BalanceBelowReserves_ShowsDeficitPerAllocationAndFund()
    {
        var fund = CreateFund();
        fund.Reserve(WalletA, Usd, 300m);
        fund.Reserve(WalletB, Usd, 200m);
        var repository = Substitute.For<IFundRepository>();
        // WalletA also holds 100 of another fund: reserved 400, balance 250 → deficit 150.
        repository.GetReservedByWalletAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WalletReservation(WalletA, Usd, 400m),
            new WalletReservation(WalletB, Usd, 200m),
        ]);
        var builder = new FundViewBuilder(repository, Wallets((WalletA, 250m), (WalletB, 1000m)));

        var dto = await builder.BuildAsync(fund, CancellationToken.None);

        dto.ReservedAmount.Should().Be(500m);
        dto.Progress.Should().Be(0.5m);
        dto.Allocations.Single(a => a.WalletId == WalletA.Value).DeficitAmount.Should().Be(150m);
        dto.Allocations.Single(a => a.WalletId == WalletB.Value).DeficitAmount.Should().Be(0m);
        dto.DeficitAmount.Should().Be(150m);
    }

    [Fact]
    public async Task WalletSummaries_ShowBalanceReservedFreeAndDeficit()
    {
        var repository = Substitute.For<IFundRepository>();
        repository.GetReservedByWalletAsync(Arg.Any<CancellationToken>()).Returns([new WalletReservation(WalletA, Usd, 400m)]);
        var builder = new FundViewBuilder(repository, Wallets((WalletA, 250m), (WalletB, 1000m)));

        var summaries = await builder.BuildWalletSummariesAsync(CancellationToken.None);

        summaries.Should().BeEquivalentTo(new[]
        {
            new WalletReservationDto(WalletA.Value, Usd.Value, 250m, 400m, -150m, 150m),
            new WalletReservationDto(WalletB.Value, Usd.Value, 1000m, 0m, 1000m, 0m),
        });
    }
}
