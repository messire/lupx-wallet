using FluentAssertions;
using LupexWallet.BalanceHistory.Application;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.BalanceHistory.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.BalanceHistory;

/// <summary>
/// CorrectionRecalculatedHandler (ADR-0003, §6): a system change of a correction amount triggers
/// the same cascade as an operation edit, from the correction date. The recalculation service is
/// real; its ports are faked.
/// </summary>
public sealed class OperationEventHandlersTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly IBalanceSnapshotRepository _repository = Substitute.For<IBalanceSnapshotRepository>();
    private readonly IBalanceHistoryUnitOfWork _unitOfWork = Substitute.For<IBalanceHistoryUnitOfWork>();
    private readonly IWalletBalanceGateway _walletGateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletOperationsLookup _operationsLookup = Substitute.For<IWalletOperationsLookup>();

    private CorrectionRecalculatedHandler CreateHandler() =>
        new(new BalanceRecalculationService(_repository, _unitOfWork, _walletGateway, _operationsLookup));

    [Fact]
    public async Task CorrectionRecalculated_RecalculatesWalletFromCorrectionDate()
    {
        var walletId = WalletId.New();
        var correctionDate = Today.AddDays(-1);
        _walletGateway.GetAsync(walletId, Arg.Any<CancellationToken>()).Returns(new WalletBalanceInfo(
            walletId, Currency, new Money(100m, Currency), new Money(0m, Currency), Today.AddYears(-1), IsArchived: false));
        _operationsLookup.GetDailyDeltasUpToAsync(walletId, Today, Arg.Any<CancellationToken>())
            .Returns(new List<DailyOperationsDelta> { new(Today.AddDays(-5), 10m), new(correctionDate, 40m) });
        _repository.GetRangeAsync(walletId, correctionDate, Today, Arg.Any<CancellationToken>())
            .Returns(new List<BalanceSnapshot>());

        var added = new List<BalanceSnapshot>();
        _repository.When(r => r.Add(Arg.Any<BalanceSnapshot>())).Do(call => added.Add(call.Arg<BalanceSnapshot>()));

        await CreateHandler().Handle(
            new DomainEventNotification<CorrectionRecalculated>(
                new CorrectionRecalculated(OperationId.New(), walletId, correctionDate)),
            CancellationToken.None);

        await _unitOfWork.Received(1).AcquireWalletLockAsync(walletId, Arg.Any<CancellationToken>());
        added.Should().HaveCount(2);
        added.Select(s => s.SnapshotDate).Should().BeEquivalentTo(new[] { correctionDate, Today });
        added.Should().OnlyContain(s => s.Balance.Amount == 150m, "100 (initial) + 10 + 40 (correction included)");
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
