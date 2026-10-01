using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// SetStatedBalanceCommandHandler (UC-24, ADR-0003, §6): the first statement of a date creates the
/// correction row, a repeated one replaces the stated value in the same row; the discrepancy is
/// recalculated in the same command and reaches the wallet as one net delta.
/// </summary>
public sealed class SetStatedBalanceCommandHandlerTests
{
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly OperationJournalFake _journal = new();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IWalletCorrectionsReconciler _reconciler = Substitute.For<IWalletCorrectionsReconciler>();
    private readonly WalletId _walletId = WalletId.New();
    private readonly List<string> _calls = [];

    private SetStatedBalanceCommandHandler CreateHandler(IWalletCorrectionsReconciler? reconciler = null) =>
        new(_journal.Repository, _unitOfWork, _journal.Gateway, reconciler ?? _reconciler);

    private SetStatedBalanceCommand Command(DateOnly date, decimal amount) => new(_walletId.Value, date, amount);

    private static Money Money(decimal amount) => new(amount, OperationJournalFake.Currency);

    [Fact]
    public async Task FirstStatement_CreatesCorrectionInTheWalletCurrencyAndReportsCreated()
    {
        _journal.ArrangeWallet(_walletId);
        Operation? added = null;
        _journal.Repository.When(r => r.Add(Arg.Any<Operation>())).Do(call => added = call.Arg<Operation>());
        _journal.Repository.GetCorrectionAsync(_walletId, Today.AddDays(-3), Arg.Any<CancellationToken>()).Returns((Operation?)null);

        var result = await CreateHandler().Handle(Command(Today.AddDays(-3), 350m), CancellationToken.None);

        result.Created.Should().BeTrue();
        added.Should().NotBeNull();
        added!.IsCorrection.Should().BeTrue();
        added.StatedBalance.Should().Be(Money(350m));
        added.OperationDate.Should().Be(Today.AddDays(-3));
        added.WalletId.Should().Be(_walletId);
        added.OperationTypeId.Should().BeNull();
        result.Operation.Id.Should().Be(added.Id.Value);
        result.Operation.IsCorrection.Should().BeTrue();
        result.Operation.StatedBalanceAmount.Should().Be(350m);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RepeatedStatement_ReplacesTheValueInTheSameRowAndReportsNotCreated()
    {
        _journal.ArrangeWallet(_walletId);
        var existing = _journal.PersistedCorrection(_walletId, Today.AddDays(-3), 350m, 50m);
        _journal.Repository.GetCorrectionAsync(_walletId, Today.AddDays(-3), Arg.Any<CancellationToken>()).Returns(existing);

        var result = await CreateHandler().Handle(Command(Today.AddDays(-3), 400m), CancellationToken.None);

        result.Created.Should().BeFalse();
        result.Operation.Id.Should().Be(existing.Id.Value, "the row is the same, the id does not change");
        existing.StatedBalance.Should().Be(Money(400m));
        existing.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OperationUpdated>();
        _journal.Repository.DidNotReceive().Add(Arg.Any<Operation>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SameValueAgain_RaisesNoEventAndDoesNotTouchTheWallet()
    {
        _journal.ArrangeWallet(_walletId);
        var existing = _journal.PersistedCorrection(_walletId, Today.AddDays(-3), 350m, 50m);
        _journal.Repository.GetCorrectionAsync(_walletId, Today.AddDays(-3), Arg.Any<CancellationToken>()).Returns(existing);
        _reconciler.ReconcileAsync(_walletId, Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>()).Returns(0m);

        var result = await CreateHandler().Handle(Command(Today.AddDays(-3), 350m), CancellationToken.None);

        result.Created.Should().BeFalse();
        existing.DomainEvents.Should().BeEmpty("the same value changes nothing: no events, so no audit entry and no cascade");
        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_PassesTheCorrectionToTheReconcilerAndAppliesItsNetChangeOnce()
    {
        _journal.ArrangeWallet(_walletId);
        _journal.Repository.GetCorrectionAsync(_walletId, Today, Arg.Any<CancellationToken>()).Returns((Operation?)null);
        _reconciler.ReconcileAsync(_walletId, Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>()).Returns(-150m);

        await CreateHandler().Handle(Command(Today, 600m), CancellationToken.None);

        await _reconciler.Received(1).ReconcileAsync(
            _walletId, Arg.Is<IReadOnlyCollection<Operation>>(x => x.Count == 1 && x.Single().IsCorrection), Arg.Any<CancellationToken>());
        await _journal.Gateway.Received(1).ApplyDeltaAsync(_walletId, Money(-150m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithTheRealReconciler_TheAdrExampleStatementReachesTheWalletAsOneDelta()
    {
        // +500 on the 1st, -200 on the 10th, 350 stated on the 10th: the correction is +50, the wallet gets +50.
        var sep10 = Today.AddDays(-10);
        _journal.ArrangeWallet(_walletId, initialBalance: 0m, currentBalance: 300m);
        _journal.PersistedOperation(_walletId, Today.AddDays(-19), 500m);
        _journal.PersistedOperation(_walletId, sep10, -200m);
        Operation? added = null;
        _journal.Repository.When(r => r.Add(Arg.Any<Operation>())).Do(call => added = call.Arg<Operation>());
        var reconciler = new WalletCorrectionsReconciler(_journal.Repository, _journal.Gateway);

        var result = await CreateHandler(reconciler).Handle(Command(sep10, 350m), CancellationToken.None);

        result.Operation.Amount.Should().Be(50m);
        added!.Amount.Should().Be(Money(50m));
        await _journal.Gateway.Received(1).ApplyDeltaAsync(_walletId, Money(50m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task NegativeAmount_IsAccepted()
    {
        _journal.ArrangeWallet(_walletId);
        _journal.Repository.GetCorrectionAsync(_walletId, Today, Arg.Any<CancellationToken>()).Returns((Operation?)null);

        var result = await CreateHandler().Handle(Command(Today, -80m), CancellationToken.None);

        result.Operation.StatedBalanceAmount.Should().Be(-80m);
    }

    [Fact]
    public async Task DateBeforeAccountingStart_ThrowsBeforeAnyChange()
    {
        _journal.ArrangeWallet(_walletId, accountingStartDate: Today.AddDays(-5));

        var act = () => CreateHandler().Handle(Command(Today.AddDays(-6), 100m), CancellationToken.None);

        await act.Should().ThrowAsync<OperationDateOutOfRangeException>();
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task DateInTheFuture_ThrowsBeforeAnyChange()
    {
        _journal.ArrangeWallet(_walletId);

        var act = () => CreateHandler().Handle(Command(Today.AddDays(1), 100m), CancellationToken.None);

        await act.Should().ThrowAsync<OperationDateOutOfRangeException>();
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task UnknownWallet_ThrowsWalletReferenceNotFoundExceptionBeforeAnyChange()
    {
        _journal.Gateway.GetAsync(_walletId, Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);

        var act = () => CreateHandler().Handle(Command(Today, 100m), CancellationToken.None);

        await act.Should().ThrowAsync<WalletReferenceNotFoundException>();
        await AssertNothingChangedAsync();
    }

    [Fact]
    public async Task Handle_TakesTheWalletLockBeforeReadingAnything()
    {
        _journal.ArrangeWallet(_walletId);
        _unitOfWork.When(u => u.AcquireWalletLocksAsync(Arg.Any<IEnumerable<WalletId>>(), Arg.Any<CancellationToken>()))
            .Do(call => _calls.Add("lock:" + string.Join(",", call.Arg<IEnumerable<WalletId>>().Select(x => x.Value))));
        _journal.Gateway.When(g => g.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>())).Do(_ => _calls.Add("read-wallet"));
        _journal.Repository.When(r => r.GetCorrectionAsync(Arg.Any<WalletId>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()))
            .Do(_ => _calls.Add("read-correction"));

        await CreateHandler().Handle(Command(Today, 100m), CancellationToken.None);

        _calls.Should().Equal($"lock:{_walletId.Value}", "read-wallet", "read-correction");
    }

    [Fact]
    public async Task ReconcilerFailure_DoesNotSaveAndDoesNotTouchTheWallet()
    {
        _journal.ArrangeWallet(_walletId);
        _reconciler.ReconcileAsync(Arg.Any<WalletId>(), Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>())
            .Returns<decimal>(_ => throw new InvalidOperationException("boom"));

        var act = () => CreateHandler().Handle(Command(Today, 100m), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    private async Task AssertNothingChangedAsync()
    {
        _journal.Repository.DidNotReceive().Add(Arg.Any<Operation>());
        await _reconciler.DidNotReceive().ReconcileAsync(
            Arg.Any<WalletId>(), Arg.Any<IReadOnlyCollection<Operation>>(), Arg.Any<CancellationToken>());
        await _journal.Gateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
