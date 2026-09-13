using FluentAssertions;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.BalanceHistory;

/// <summary>
/// BalanceRecalculationService (ddd-model.md, §2.6; ADR-0003/ADR-0004) — единый алгоритм
/// каскадного пересчета: InitialBalance + сумма AppliedDelta операций до каждой даты,
/// диапазон [max(fromDate, AccountingStartDate) .. сегодня], сериализация через advisory lock.
/// </summary>
public sealed class BalanceRecalculationServiceTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());

    private readonly IBalanceSnapshotRepository _repository = Substitute.For<IBalanceSnapshotRepository>();
    private readonly IBalanceHistoryUnitOfWork _unitOfWork = Substitute.For<IBalanceHistoryUnitOfWork>();
    private readonly IWalletBalanceGateway _walletGateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletOperationsLookup _operationsLookup = Substitute.For<IWalletOperationsLookup>();

    private BalanceRecalculationService CreateService() =>
        new(_repository, _unitOfWork, _walletGateway, _operationsLookup);

    private static WalletBalanceInfo Wallet(WalletId id, decimal initialBalance, DateOnly accountingStartDate) => new(
        id, Currency, new Money(initialBalance, Currency), new Money(0m, Currency), accountingStartDate, IsArchived: false);

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    [Fact]
    public async Task RecalculateFromAsync_AlwaysAcquiresWalletLockBeforeReadingWallet()
    {
        var walletId = WalletId.New();
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);

        await CreateService().RecalculateFromAsync(walletId, Today, CancellationToken.None);

        await _unitOfWork.Received(1).AcquireWalletLockAsync(walletId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateFromAsync_WalletNotFound_ReturnsWithoutThrowingOrSaving()
    {
        var walletId = WalletId.New();
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);

        var act = () => CreateService().RecalculateFromAsync(walletId, Today, CancellationToken.None);

        await act.Should().NotThrowAsync();
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateFromAsync_FromDateAfterToday_DoesNothing()
    {
        var walletId = WalletId.New();
        var wallet = Wallet(walletId, 0m, Today.AddYears(-1));
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(wallet);

        await CreateService().RecalculateFromAsync(walletId, Today.AddDays(5), CancellationToken.None);

        _repository.DidNotReceive().Add(Arg.Any<BalanceSnapshot>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateFromAsync_FromDateBeforeAccountingStartDate_ClampsRangeToAccountingStartDate()
    {
        var walletId = WalletId.New();
        var accountingStartDate = Today; // диапазон схлопывается к одному дню (сегодня).
        var wallet = Wallet(walletId, 1000m, accountingStartDate);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(wallet);
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<DailyOperationsDelta>());
        _repository.GetRangeAsync(walletId, accountingStartDate, Today, Arg.Any<CancellationToken>())
            .Returns(new List<BalanceSnapshot>());

        await CreateService().RecalculateFromAsync(walletId, accountingStartDate.AddYears(-1), CancellationToken.None);

        _repository.Received(1).Add(Arg.Is<BalanceSnapshot>(s => s.SnapshotDate == accountingStartDate && s.Balance.Amount == 1000m));
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateFromAsync_NoExistingSnapshots_CreatesOneForEachDateInRangeWithRunningBalance()
    {
        var walletId = WalletId.New();
        var fromDate = Today.AddDays(-2);
        var wallet = Wallet(walletId, 100m, Today.AddYears(-1));
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(wallet);
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>()).Returns(new List<DailyOperationsDelta>
        {
            new(fromDate, 50m),        // +50 в первый день диапазона
            new(fromDate.AddDays(1), -20m), // -20 на следующий день
        });
        _repository.GetRangeAsync(walletId, fromDate, Today, Arg.Any<CancellationToken>()).Returns(new List<BalanceSnapshot>());

        var addedSnapshots = new List<BalanceSnapshot>();
        _repository.When(r => r.Add(Arg.Any<BalanceSnapshot>())).Do(call => addedSnapshots.Add(call.Arg<BalanceSnapshot>()));

        await CreateService().RecalculateFromAsync(walletId, fromDate, CancellationToken.None);

        // Диапазон [fromDate .. today] включительно: fromDate, fromDate+1, today.
        addedSnapshots.Should().HaveCount(3);
        addedSnapshots.Single(s => s.SnapshotDate == fromDate).Balance.Amount.Should().Be(150m, "100 (InitialBalance) + 50");
        addedSnapshots.Single(s => s.SnapshotDate == fromDate.AddDays(1)).Balance.Amount.Should().Be(130m, "150 - 20");
        addedSnapshots.Single(s => s.SnapshotDate == Today).Balance.Amount.Should().Be(130m, "без дельт на последующие дни баланс не меняется");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RecalculateFromAsync_ExistingSnapshotWithChangedBalance_CallsUpdateBalance()
    {
        var walletId = WalletId.New();
        var date = Today;
        var wallet = Wallet(walletId, 100m, Today.AddYears(-1));
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(wallet);
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<DailyOperationsDelta> { new(date, 200m) }); // новый баланс = 300, был 100

        var existingSnapshot = BalanceSnapshot.Create(walletId, date, new Money(100m, Currency));
        existingSnapshot.ClearDomainEvents();
        _repository.GetRangeAsync(walletId, date, Today, Arg.Any<CancellationToken>())
            .Returns(new List<BalanceSnapshot> { existingSnapshot });

        await CreateService().RecalculateFromAsync(walletId, date, CancellationToken.None);

        existingSnapshot.Balance.Amount.Should().Be(300m);
        existingSnapshot.DomainEvents.OfType<BalanceSnapshotUpdated>().Should().ContainSingle();
        _repository.DidNotReceive().Add(Arg.Any<BalanceSnapshot>());
    }

    [Fact]
    public async Task RecalculateFromAsync_ExistingSnapshotWithUnchangedBalance_DoesNotCallUpdateBalance()
    {
        var walletId = WalletId.New();
        var date = Today;
        var wallet = Wallet(walletId, 100m, Today.AddYears(-1));
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(wallet);
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<DailyOperationsDelta>()); // без операций баланс = InitialBalance = 100, как и был

        var existingSnapshot = BalanceSnapshot.Create(walletId, date, new Money(100m, Currency));
        existingSnapshot.ClearDomainEvents();
        _repository.GetRangeAsync(walletId, date, Today, Arg.Any<CancellationToken>())
            .Returns(new List<BalanceSnapshot> { existingSnapshot });

        await CreateService().RecalculateFromAsync(walletId, date, CancellationToken.None);

        existingSnapshot.DomainEvents.Should().BeEmpty("не должно быть лишних записей аудита при отсутствии фактического изменения");
    }
}
