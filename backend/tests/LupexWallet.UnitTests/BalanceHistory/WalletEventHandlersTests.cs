using FluentAssertions;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.BalanceHistory.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.BalanceHistory;

/// <summary>
/// WalletDeletedHandler / WalletCurrencyChangedHandler (ADR-0002, §2; R11, R12): snapshots are
/// derived data — removed with the wallet (BalanceSnapshotDeleted for audit, one SaveChanges under
/// the wallet advisory lock) and, on a currency change, removed in a SEPARATE SaveChanges and then
/// rebuilt from AccountingStartDate in the new currency. Faked ports; the recalculation service is real.
/// </summary>
public sealed class WalletEventHandlersTests
{
    private static readonly CurrencyId OldCurrency = new(Guid.NewGuid());
    private static readonly CurrencyId NewCurrency = new(Guid.NewGuid());
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IBalanceSnapshotRepository _repository = Substitute.For<IBalanceSnapshotRepository>();
    private readonly IBalanceHistoryUnitOfWork _unitOfWork = Substitute.For<IBalanceHistoryUnitOfWork>();
    private readonly IWalletBalanceGateway _walletGateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletOperationsLookup _operationsLookup = Substitute.For<IWalletOperationsLookup>();

    private BalanceRecalculationService CreateRecalculationService() =>
        new(_repository, _unitOfWork, _walletGateway, _operationsLookup);

    private WalletDeletedHandler CreateDeletedHandler() => new(_repository, _unitOfWork);

    private WalletCurrencyChangedHandler CreateCurrencyChangedHandler() =>
        new(_repository, _unitOfWork, _walletGateway, CreateRecalculationService());

    private static BalanceSnapshot Snapshot(WalletId walletId, DateOnly date, decimal balance, CurrencyId? currency = null) =>
        BalanceSnapshot.Create(walletId, date, new Money(balance, currency ?? OldCurrency));

    private static WalletBalanceInfo Wallet(WalletId id, CurrencyId currency, DateOnly accountingStartDate) => new(
        id, currency, new Money(100m, currency), new Money(100m, currency), accountingStartDate, IsArchived: false);

    private void ArrangeSnapshots(WalletId walletId, params BalanceSnapshot[] snapshots) =>
        _repository.GetAllForWalletAsync(walletId, Arg.Any<CancellationToken>()).Returns(snapshots);

    // ---- WalletDeleted ----

    [Fact]
    public async Task WalletDeleted_MarksEveryLoadedSnapshotAsDeletedRemovesItAndSavesOnce()
    {
        var walletId = WalletId.New();
        var first = Snapshot(walletId, Today.AddDays(-2), 100m);
        var second = Snapshot(walletId, Today.AddDays(-1), 150m);
        var third = Snapshot(walletId, Today, 150m);
        ArrangeSnapshots(walletId, first, second, third);

        await CreateDeletedHandler().Handle(
            new DomainEventNotification<WalletDeleted>(new WalletDeleted(walletId)), CancellationToken.None);

        foreach (var snapshot in new[] { first, second, third })
        {
            _repository.Received(1).Remove(snapshot);
            var deleted = snapshot.DomainEvents.OfType<BalanceSnapshotDeleted>().Should().ContainSingle().Subject;
            deleted.WalletId.Should().Be(walletId);
            deleted.SnapshotDate.Should().Be(snapshot.SnapshotDate);
            deleted.Balance.Should().Be(snapshot.Balance, "the event carries the value before removal");
        }

        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletDeleted_AcquiresWalletLockBeforeLoadingSnapshots()
    {
        var walletId = WalletId.New();
        ArrangeSnapshots(walletId, Snapshot(walletId, Today, 10m));

        await CreateDeletedHandler().Handle(
            new DomainEventNotification<WalletDeleted>(new WalletDeleted(walletId)), CancellationToken.None);

        Received.InOrder(() =>
        {
            _unitOfWork.AcquireWalletLockAsync(walletId, Arg.Any<CancellationToken>());
            _repository.GetAllForWalletAsync(walletId, Arg.Any<CancellationToken>());
            _unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task WalletDeleted_NoSnapshots_DoesNotRemoveOrSave()
    {
        var walletId = WalletId.New();
        ArrangeSnapshots(walletId);

        await CreateDeletedHandler().Handle(
            new DomainEventNotification<WalletDeleted>(new WalletDeleted(walletId)), CancellationToken.None);

        await _unitOfWork.Received(1).AcquireWalletLockAsync(walletId, Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Remove(Arg.Any<BalanceSnapshot>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletDeleted_DoesNotRecalculateAnything()
    {
        var walletId = WalletId.New();
        ArrangeSnapshots(walletId, Snapshot(walletId, Today, 10m));

        await CreateDeletedHandler().Handle(
            new DomainEventNotification<WalletDeleted>(new WalletDeleted(walletId)), CancellationToken.None);

        await _walletGateway.DidNotReceive().GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<BalanceSnapshot>());
    }

    // ---- WalletCurrencyChanged ----

    [Fact]
    public async Task WalletCurrencyChanged_RemovesOldSnapshotsWithSeparateSaveThenRecalculatesFromAccountingStartDate()
    {
        var walletId = WalletId.New();
        var accountingStartDate = Today.AddDays(-2);
        var oldSnapshots = new[]
        {
            Snapshot(walletId, accountingStartDate, 100m),
            Snapshot(walletId, accountingStartDate.AddDays(1), 100m),
            Snapshot(walletId, Today, 100m),
        };
        ArrangeSnapshots(walletId, oldSnapshots);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(Wallet(walletId, NewCurrency, accountingStartDate));
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>()).Returns(new List<DailyOperationsDelta>());
        _repository.GetRangeAsync(walletId, accountingStartDate, Today, Arg.Any<CancellationToken>()).Returns(new List<BalanceSnapshot>());
        var added = new List<BalanceSnapshot>();
        var callLog = new List<string>();
        _repository.When(r => r.Add(Arg.Any<BalanceSnapshot>())).Do(call =>
        {
            added.Add(call.Arg<BalanceSnapshot>());
            callLog.Add("add");
        });
        _unitOfWork.When(u => u.SaveChangesAsync(Arg.Any<CancellationToken>())).Do(_ => callLog.Add("save"));

        await CreateCurrencyChangedHandler().Handle(
            new DomainEventNotification<WalletCurrencyChanged>(new WalletCurrencyChanged(walletId, OldCurrency, NewCurrency)),
            CancellationToken.None);

        foreach (var snapshot in oldSnapshots)
        {
            _repository.Received(1).Remove(snapshot);
            snapshot.DomainEvents.OfType<BalanceSnapshotDeleted>().Should().ContainSingle();
        }

        // Deleting and inserting the same (WalletId, SnapshotDate) key must not share one change set (R12):
        // the first save (removals) precedes the first insert, the second save (inserts) follows it.
        callLog.Should().StartWith(["save"]);
        callLog.IndexOf("add").Should().BeGreaterThan(callLog.IndexOf("save"));
        callLog.Last().Should().Be("save");
        await _unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());

        added.Should().HaveCount(3, "one snapshot per day from AccountingStartDate through today");
        added.Select(s => s.SnapshotDate).Should().BeEquivalentTo([accountingStartDate, accountingStartDate.AddDays(1), Today]);
        added.Should().OnlyContain(s => s.CurrencyId == NewCurrency && s.Balance.Amount == 100m);
    }

    [Fact]
    public async Task WalletCurrencyChanged_NoOldSnapshots_StillRebuildsFromAccountingStartDateWithSingleSave()
    {
        var walletId = WalletId.New();
        var accountingStartDate = Today;
        ArrangeSnapshots(walletId);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(Wallet(walletId, NewCurrency, accountingStartDate));
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>()).Returns(new List<DailyOperationsDelta>());
        _repository.GetRangeAsync(walletId, accountingStartDate, Today, Arg.Any<CancellationToken>()).Returns(new List<BalanceSnapshot>());

        await CreateCurrencyChangedHandler().Handle(
            new DomainEventNotification<WalletCurrencyChanged>(new WalletCurrencyChanged(walletId, OldCurrency, NewCurrency)),
            CancellationToken.None);

        _repository.DidNotReceive().Remove(Arg.Any<BalanceSnapshot>());
        _repository.Received(1).Add(Arg.Is<BalanceSnapshot>(s => s.SnapshotDate == accountingStartDate && s.CurrencyId == NewCurrency));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WalletCurrencyChanged_WalletNoLongerExists_RemovesSnapshotsAndDoesNotRecalculate()
    {
        var walletId = WalletId.New();
        ArrangeSnapshots(walletId, Snapshot(walletId, Today, 10m));
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);

        var act = () => CreateCurrencyChangedHandler().Handle(
            new DomainEventNotification<WalletCurrencyChanged>(new WalletCurrencyChanged(walletId, OldCurrency, NewCurrency)),
            CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        _repository.DidNotReceive().Add(Arg.Any<BalanceSnapshot>());
        await _operationsLookup.DidNotReceive().GetDailyDeltasUpToAsync(Arg.Any<WalletId>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>());
    }

    // ---- OperationRestored ----

    [Fact]
    public async Task OperationRestored_RecalculatesWalletFromOperationDateThroughToday()
    {
        var walletId = WalletId.New();
        var operationDate = Today.AddDays(-3);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(Wallet(walletId, OldCurrency, Today.AddYears(-1)));
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>()).Returns(new List<DailyOperationsDelta>
        {
            new(operationDate, 50m),
        });
        _repository.GetRangeAsync(walletId, operationDate, Today, Arg.Any<CancellationToken>()).Returns(new List<BalanceSnapshot>());
        var added = new List<BalanceSnapshot>();
        _repository.When(r => r.Add(Arg.Any<BalanceSnapshot>())).Do(call => added.Add(call.Arg<BalanceSnapshot>()));
        var handler = new OperationRestoredHandler(CreateRecalculationService());

        await handler.Handle(
            new DomainEventNotification<LupexWallet.Operations.Domain.OperationRestored>(
                new LupexWallet.Operations.Domain.OperationRestored(OperationId.New(), walletId, operationDate)),
            CancellationToken.None);

        await _unitOfWork.Received(1).AcquireWalletLockAsync(walletId, Arg.Any<CancellationToken>());
        await _repository.Received(1).GetRangeAsync(walletId, operationDate, Today, Arg.Any<CancellationToken>());
        added.Select(s => s.SnapshotDate).Should().BeEquivalentTo(
            Enumerable.Range(0, 4).Select(i => operationDate.AddDays(i)), "from the operation date (not from today) through today");
        added.Should().OnlyContain(s => s.Balance.Amount == 150m, "100 (initial) + 50 (the restored operation is counted again)");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
