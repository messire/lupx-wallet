using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// DeleteOperationCommandHandler — реверс применённой дельты (обратный знак AppliedDelta)
/// и защита операций — частей перевода (ddd-model.md, §5: "операцию с TransferId нельзя
/// удалить в обход агрегата Transfer").
/// </summary>
public sealed class DeleteOperationCommandHandlerTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();

    private DeleteOperationCommandHandler CreateHandler() => new(_operationRepository, _unitOfWork, _walletBalanceGateway);

    private static Operation Income(decimal amount) => Operation.Create(
        OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
        Money(amount), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(amount));

    [Fact]
    public async Task Handle_UnknownOperation_ThrowsKeyNotFoundException()
    {
        _operationRepository.GetByIdAsync(Arg.Any<OperationId>(), Arg.Any<CancellationToken>()).Returns((Operation?)null);
        var command = new DeleteOperationCommand(Guid.NewGuid());

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_OperationPartOfTransfer_ThrowsOperationPartOfTransferExceptionAndDoesNotTouchBalance()
    {
        var (_, sourceOperation, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));
        _operationRepository.GetByIdAsync(sourceOperation.Id, Arg.Any<CancellationToken>()).Returns(sourceOperation);
        var command = new DeleteOperationCommand(sourceOperation.Id.Value);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationPartOfTransferException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_OperationDatedBeforeToday_ThrowsOperationDeletionNotAllowedExceptionAndDoesNotTouchBalance()
    {
        // ADR-0009, случай b — решение пользователя от 2026-09-11: дата строго раньше сегодня
        // уже считается отраженной в истории баланса и не подлежит удалению.
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var operation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
            Money(70m), yesterday, adjustmentMode: null, appliedDelta: Money(70m));
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        var command = new DeleteOperationCommand(operation.Id.Value);

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<OperationDeletionNotAllowedException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StandaloneOperation_ReversesAppliedDeltaAndRemovesOperation()
    {
        var operation = Income(70m);
        _operationRepository.GetByIdAsync(operation.Id, Arg.Any<CancellationToken>()).Returns(operation);
        var command = new DeleteOperationCommand(operation.Id.Value);

        await CreateHandler().Handle(command, CancellationToken.None);

        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            operation.WalletId, Arg.Is<Money>(m => m.Amount == -70m), Arg.Any<CancellationToken>());
        _operationRepository.Received(1).Remove(operation);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
