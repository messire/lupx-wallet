using FluentAssertions;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.Operations.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.BalanceHistory;

/// <summary>
/// BackfillWalletBalanceSnapshotsCommandHandler (ADR-0001, §4/§6; ADR-0003) — вычисляет
/// fromDate (последний слепок + 1 либо AccountingStartDate) и делегирует расчёт единому
/// BalanceRecalculationService. Команда ICommand, поэтому TransactionBehavior оборачивает её
/// в одну транзакцию на кошелёк.
/// </summary>
public sealed class BackfillWalletBalanceSnapshotsCommandHandlerTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IBalanceSnapshotRepository _repository = Substitute.For<IBalanceSnapshotRepository>();
    private readonly IBalanceHistoryUnitOfWork _unitOfWork = Substitute.For<IBalanceHistoryUnitOfWork>();
    private readonly IWalletBalanceGateway _walletGateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletOperationsLookup _operationsLookup = Substitute.For<IWalletOperationsLookup>();

    private BackfillWalletBalanceSnapshotsCommandHandler CreateHandler() => new(
        _repository,
        _walletGateway,
        new BalanceRecalculationService(_repository, _unitOfWork, _walletGateway, _operationsLookup));

    private static WalletBalanceInfo Wallet(WalletId id, decimal initialBalance, DateOnly accountingStartDate) => new(
        id, Currency, new Money(initialBalance, Currency), new Money(0m, Currency), accountingStartDate, IsArchived: false);

    [Fact]
    public void Command_IsMarkedAsCommand_SoThatTransactionBehaviorApplies()
    {
        var command = new BackfillWalletBalanceSnapshotsCommand(WalletId.New());

        command.Should().BeAssignableTo<ICommand<Unit>>();
        command.Should().BeAssignableTo<IRequest>();
    }

    [Fact]
    public async Task Handle_WalletNotFound_DoesNothing()
    {
        var walletId = WalletId.New();
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);

        await CreateHandler().Handle(new BackfillWalletBalanceSnapshotsCommand(walletId), CancellationToken.None);

        await _unitOfWork.DidNotReceive().AcquireWalletLockAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NoSnapshotsYet_RecalculatesFromAccountingStartDateThroughToday()
    {
        var walletId = WalletId.New();
        var accountingStartDate = Today.AddDays(-2);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(Wallet(walletId, 100m, accountingStartDate));
        _repository.GetLatestSnapshotDateAsync(walletId, Arg.Any<CancellationToken>()).Returns((DateOnly?)null);
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<DailyOperationsDelta>());
        _repository.GetRangeAsync(walletId, accountingStartDate, Today, Arg.Any<CancellationToken>())
            .Returns(new List<BalanceSnapshot>());

        await CreateHandler().Handle(new BackfillWalletBalanceSnapshotsCommand(walletId), CancellationToken.None);

        await _unitOfWork.Received(1).AcquireWalletLockAsync(walletId, Arg.Any<CancellationToken>());
        _repository.Received(3).Add(Arg.Any<BalanceSnapshot>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LatestSnapshotExists_RecalculatesFromTheDayAfterIt()
    {
        var walletId = WalletId.New();
        var latestSnapshotDate = Today.AddDays(-2);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(Wallet(walletId, 100m, Today.AddYears(-1)));
        _repository.GetLatestSnapshotDateAsync(walletId, Arg.Any<CancellationToken>()).Returns(latestSnapshotDate);
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<DailyOperationsDelta>());
        _repository.GetRangeAsync(walletId, latestSnapshotDate.AddDays(1), Today, Arg.Any<CancellationToken>())
            .Returns(new List<BalanceSnapshot>());

        await CreateHandler().Handle(new BackfillWalletBalanceSnapshotsCommand(walletId), CancellationToken.None);

        // Диапазон [latest + 1 .. today] = 2 дня; сам последний слепок не пересчитывается.
        _repository.Received(2).Add(Arg.Is<BalanceSnapshot>(s => s.SnapshotDate > latestSnapshotDate));
        await _repository.Received(1).GetRangeAsync(walletId, latestSnapshotDate.AddDays(1), Today, Arg.Any<CancellationToken>());
    }
}
