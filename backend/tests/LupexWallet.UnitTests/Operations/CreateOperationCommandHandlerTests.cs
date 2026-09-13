using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// CreateOperationCommandHandler — оркестрация лукапов (ReferenceData/Wallets) и применение
/// дельты к балансу (ADR-0007) через моки портов. Регресс: попытка создать операцию с
/// behaviorKind=Transfer напрямую должна давать доменное исключение (400/409), а не 500
/// (docs/PROGRESS.md, "Баги, найденные и исправленные").
/// </summary>
public sealed class CreateOperationCommandHandlerTests
{
    private static readonly CurrencyId WalletCurrency = new(Guid.NewGuid());

    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IOperationTypeLookup _operationTypeLookup = Substitute.For<IOperationTypeLookup>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();

    private CreateOperationCommandHandler CreateHandler() =>
        new(_operationRepository, _unitOfWork, _operationTypeLookup, _walletBalanceGateway);

    private static WalletBalanceInfo Wallet(decimal currentBalance = 0m, DateOnly? accountingStartDate = null) => new(
        WalletId.New(), WalletCurrency, new Money(0m, WalletCurrency), new Money(currentBalance, WalletCurrency),
        accountingStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), IsArchived: false);

    private static OperationTypeLookupResult ActiveType(string behaviorCode) =>
        new(OperationTypeId.New(), "Тип", new OperationBehaviorKindId(Guid.NewGuid()), behaviorCode, IsActive: true);

    [Fact]
    public async Task Handle_OperationTypeNotFound_ThrowsOperationTypeReferenceNotFoundException()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns((OperationTypeLookupResult?)null);
        var command = new CreateOperationCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationTypeReferenceNotFoundException>();
    }

    [Fact]
    public async Task Handle_OperationTypeInactive_ThrowsOperationTypeReferenceInactiveException()
    {
        var inactiveType = ActiveType("Income") with { IsActive = false };
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(inactiveType);
        var command = new CreateOperationCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationTypeReferenceInactiveException>();
    }

    [Fact]
    public async Task Handle_WalletNotFound_ThrowsWalletReferenceNotFoundException()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);
        var command = new CreateOperationCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<WalletReferenceNotFoundException>();
    }

    [Fact]
    public async Task Handle_OperationTypeWithTransferBehavior_ThrowsDirectOperationCreationNotAllowedForBehaviorException()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Transfer"));
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(Wallet());
        var command = new CreateOperationCommand(Guid.NewGuid(), Guid.NewGuid(), 10m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<DirectOperationCreationNotAllowedForBehaviorException>();
    }

    [Fact]
    public async Task Handle_OperationDateBeforeAccountingStartDate_ThrowsOperationDateOutOfRangeException()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var wallet = Wallet(accountingStartDate: DateOnly.FromDateTime(DateTime.UtcNow));
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(wallet);
        var command = new CreateOperationCommand(
            Guid.NewGuid(), Guid.NewGuid(), 10m, wallet.AccountingStartDate.AddDays(-1), null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationDateOutOfRangeException>();
    }

    [Fact]
    public async Task Handle_ValidIncome_AppliesPositiveDeltaAndPersistsOperation()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var wallet = Wallet(currentBalance: 100m);
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(wallet);
        var command = new CreateOperationCommand(wallet.Id.Value, Guid.NewGuid(), 40m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        var dto = await CreateHandler().Handle(command, CancellationToken.None);

        dto.Amount.Should().Be(40m);
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            wallet.Id, Arg.Is<Money>(m => m.Amount == 40m), Arg.Any<CancellationToken>());
        _operationRepository.Received(1).Add(Arg.Any<Operation>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidExpense_AppliesNegativeDelta()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Expense"));
        var wallet = Wallet(currentBalance: 100m);
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(wallet);
        var command = new CreateOperationCommand(wallet.Id.Value, Guid.NewGuid(), 30m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            wallet.Id, Arg.Is<Money>(m => m.Amount == -30m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdjustmentAbsolute_AppliesDeltaComputedFromCurrentBalance()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Adjustment"));
        var wallet = Wallet(currentBalance: 100m);
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(wallet);
        // Хочет получить баланс 250 при текущем 100 -> дельта 150.
        var command = new CreateOperationCommand(wallet.Id.Value, Guid.NewGuid(), 250m, DateOnly.FromDateTime(DateTime.UtcNow), AdjustmentMode.Absolute);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            wallet.Id, Arg.Is<Money>(m => m.Amount == 150m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_AdjustmentDelta_AppliesRequestedAmountAsIs()
    {
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Adjustment"));
        var wallet = Wallet(currentBalance: 100m);
        _walletBalanceGateway.GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>()).Returns(wallet);
        var command = new CreateOperationCommand(wallet.Id.Value, Guid.NewGuid(), -25m, DateOnly.FromDateTime(DateTime.UtcNow), AdjustmentMode.Delta);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            wallet.Id, Arg.Is<Money>(m => m.Amount == -25m), Arg.Any<CancellationToken>());
    }
}
