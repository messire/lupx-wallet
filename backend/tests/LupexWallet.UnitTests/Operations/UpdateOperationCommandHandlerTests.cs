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
/// UpdateOperationCommandHandler — редактирование операции на том же кошельке (реверс +
/// повторное применение инкрементной дельты, ADR-0007) и перенос операции на другой
/// кошелек (UC-13, решение пользователя от 2026-09-11): эквивалент удаления со старого
/// кошелька + создания на новом — см. CreateOperationCommandHandlerTests/
/// DeleteOperationCommandHandlerTests для сравнения с обоими исходными сценариями.
/// </summary>
public sealed class UpdateOperationCommandHandlerTests
{
    private static readonly CurrencyId WalletCurrency = new(Guid.NewGuid());

    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IOperationTypeLookup _operationTypeLookup = Substitute.For<IOperationTypeLookup>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();

    private UpdateOperationCommandHandler CreateHandler() =>
        new(_operationRepository, _unitOfWork, _operationTypeLookup, _walletBalanceGateway);

    private static Money Money(decimal amount, CurrencyId? currency = null) => new(amount, currency ?? WalletCurrency);

    private static WalletBalanceInfo Wallet(WalletId id, decimal currentBalance = 0m, CurrencyId? currency = null, DateOnly? accountingStartDate = null) => new(
        id, currency ?? WalletCurrency, new Money(0m, currency ?? WalletCurrency), new Money(currentBalance, currency ?? WalletCurrency),
        accountingStartDate ?? DateOnly.FromDateTime(DateTime.UtcNow).AddYears(-1), IsArchived: false);

    private static OperationTypeLookupResult ActiveType(string behaviorCode) =>
        new(OperationTypeId.New(), "Тип", new OperationBehaviorKindId(Guid.NewGuid()), behaviorCode, IsActive: true);

    private static Operation Income(WalletId walletId, decimal amount, DateOnly? date = null) => Operation.Create(
        OperationId.New(), walletId, OperationTypeId.New(), OperationEffectKind.Income,
        Money(amount), date ?? DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(amount));

    [Fact]
    public async Task Handle_UnknownOperation_ThrowsKeyNotFoundException()
    {
        _operationRepository.GetByIdAsync(Arg.Any<OperationId>(), Arg.Any<CancellationToken>()).Returns((Operation?)null);
        var command = new UpdateOperationCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 10m, DateOnly.FromDateTime(DateTime.UtcNow), null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_SameWallet_AppliesIncrementalDeltaOnlyOnce()
    {
        var operation = Income(WalletId.New(), amount: 100m);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var wallet = Wallet(operation.WalletId, currentBalance: 500m);
        _walletBalanceGateway.GetAsync(operation.WalletId, Arg.Any<CancellationToken>()).Returns(wallet);
        var command = new UpdateOperationCommand(
            operation.Id.Value, operation.WalletId.Value, Guid.NewGuid(), 150m, operation.OperationDate, null);

        await CreateHandler().Handle(command, CancellationToken.None);

        // baseline = 500 - 100 (уже примененные ранее 100) = 400; intended = 150; incremental = 150 - 100 = 50.
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            operation.WalletId, Arg.Is<Money>(m => m.Amount == 50m), Arg.Any<CancellationToken>());
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(
            Arg.Is<WalletId>(w => w != operation.WalletId), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        operation.WalletId.Should().Be(wallet.Id);
    }

    [Fact]
    public async Task Handle_WalletChanged_OperationPartOfTransfer_ThrowsOperationPartOfTransferExceptionAndDoesNotTouchBalance()
    {
        var (_, sourceOperation, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));
        _operationRepository.GetByIdAsync(sourceOperation.Id, Arg.Any<CancellationToken>()).Returns(sourceOperation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var newWalletId = WalletId.New();
        _walletBalanceGateway.GetAsync(newWalletId, Arg.Any<CancellationToken>()).Returns(Wallet(newWalletId, currentBalance: 0m));
        var command = new UpdateOperationCommand(
            sourceOperation.Id.Value, newWalletId.Value, Guid.NewGuid(), 999m, sourceOperation.OperationDate, null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationPartOfTransferException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WalletChanged_NewWalletNotFound_ThrowsWalletReferenceNotFoundException()
    {
        var operation = Income(WalletId.New(), amount: 100m);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var newWalletId = WalletId.New();
        _walletBalanceGateway.GetAsync(newWalletId, Arg.Any<CancellationToken>()).Returns((WalletBalanceInfo?)null);
        var command = new UpdateOperationCommand(
            operation.Id.Value, newWalletId.Value, Guid.NewGuid(), 100m, operation.OperationDate, null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<WalletReferenceNotFoundException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WalletChanged_ReversesOldWalletAndAppliesToNewWallet()
    {
        var oldWalletId = WalletId.New();
        var newWalletId = WalletId.New();
        var operation = Income(oldWalletId, amount: 100m);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var newWallet = Wallet(newWalletId, currentBalance: 1000m);
        _walletBalanceGateway.GetAsync(newWalletId, Arg.Any<CancellationToken>()).Returns(newWallet);
        var command = new UpdateOperationCommand(
            operation.Id.Value, newWalletId.Value, Guid.NewGuid(), 250m, operation.OperationDate, null);

        var dto = await CreateHandler().Handle(command, CancellationToken.None);

        // Реверс на старом кошельке — обратный знак AppliedDelta (было 100).
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            oldWalletId, Arg.Is<Money>(m => m.Amount == -100m), Arg.Any<CancellationToken>());
        // Применение на новом — как при первичном создании: intendedDelta = 250 (Income, baseline не участвует).
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            newWalletId, Arg.Is<Money>(m => m.Amount == 250m), Arg.Any<CancellationToken>());

        operation.WalletId.Should().Be(newWalletId);
        operation.AppliedDelta.Amount.Should().Be(250m);
        dto.WalletId.Should().Be(newWalletId.Value);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WalletChanged_NewWalletDifferentCurrency_AdoptsNewWalletCurrency()
    {
        var oldWalletId = WalletId.New();
        var newWalletId = WalletId.New();
        var newCurrency = new CurrencyId(Guid.NewGuid());
        var operation = Income(oldWalletId, amount: 100m);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        var newWallet = Wallet(newWalletId, currentBalance: 0m, currency: newCurrency);
        _walletBalanceGateway.GetAsync(newWalletId, Arg.Any<CancellationToken>()).Returns(newWallet);
        var command = new UpdateOperationCommand(
            operation.Id.Value, newWalletId.Value, Guid.NewGuid(), 50m, operation.OperationDate, null);

        await CreateHandler().Handle(command, CancellationToken.None);

        operation.CurrencyId.Should().Be(newCurrency);
        operation.Amount.CurrencyId.Should().Be(newCurrency);
    }

    [Fact]
    public async Task Handle_WalletChanged_DateOutOfRangeForNewWallet_ThrowsOperationDateOutOfRangeException()
    {
        var oldWalletId = WalletId.New();
        var newWalletId = WalletId.New();
        var operationDate = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5);
        var operation = Income(oldWalletId, amount: 100m, date: operationDate);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        _operationTypeLookup.GetAsync(Arg.Any<OperationTypeId>(), Arg.Any<CancellationToken>()).Returns(ActiveType("Income"));
        // Новый кошелек начал учет позже даты операции — перенос должен быть отклонен.
        var newWallet = Wallet(newWalletId, currentBalance: 0m, accountingStartDate: DateOnly.FromDateTime(DateTime.UtcNow));
        _walletBalanceGateway.GetAsync(newWalletId, Arg.Any<CancellationToken>()).Returns(newWallet);
        var command = new UpdateOperationCommand(
            operation.Id.Value, newWalletId.Value, Guid.NewGuid(), 100m, operationDate, null);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationDateOutOfRangeException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }
}
