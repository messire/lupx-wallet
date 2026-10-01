using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// RestoreOperationCommandHandler (ADR-0002, §3, §6) — revokes soft deletion and re-applies
/// the stored AppliedDelta with its own sign; restoration is always possible (date, operation type
/// activity and wallet archival are not re-checked), and a transfer leg is restored only as a whole.
/// </summary>
public sealed class RestoreOperationCommandHandlerTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletCorrectionsReconciler _correctionsReconciler = Substitute.For<IWalletCorrectionsReconciler>();

    private RestoreOperationCommandHandler CreateHandler() => new(_operationRepository, _unitOfWork, _walletBalanceGateway, _correctionsReconciler);

    private static Operation DeletedOperation(OperationEffectKind kind, decimal amount, decimal appliedDelta, DateOnly? date = null)
    {
        var operation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), kind,
            Money(amount), date ?? DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-5),
            adjustmentMode: null, appliedDelta: Money(appliedDelta));
        operation.SoftDelete();
        return operation;
    }

    [Fact]
    public void Command_IsMarkedAsCommand_SoThatTransactionBehaviorApplies()
    {
        new RestoreOperationCommand(Guid.NewGuid()).Should().BeAssignableTo<ICommand<OperationDto>>();
    }

    [Fact]
    public async Task Handle_UnknownOperation_ThrowsKeyNotFoundException()
    {
        _operationRepository.GetByIdAsync(Arg.Any<OperationId>(), Arg.Any<CancellationToken>()).Returns((Operation?)null);

        var act = () => CreateHandler().Handle(new RestoreOperationCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_DeletedIncome_ReappliesPositiveDeltaSavesAndReturnsActiveDto()
    {
        var operation = DeletedOperation(OperationEffectKind.Income, 70m, 70m);
        _operationRepository.Found(operation);

        var result = await CreateHandler().Handle(new RestoreOperationCommand(operation.Id.Value), CancellationToken.None);

        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            operation.WalletId, Arg.Is<Money>(m => m.Amount == 70m), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        operation.IsDeleted.Should().BeFalse();
        result.Id.Should().Be(operation.Id.Value);
        result.IsDeleted.Should().BeFalse();
        result.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_DeletedExpense_ReappliesNegativeDelta()
    {
        var operation = DeletedOperation(OperationEffectKind.Expense, 40m, -40m);
        _operationRepository.Found(operation);

        await CreateHandler().Handle(new RestoreOperationCommand(operation.Id.Value), CancellationToken.None);

        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            operation.WalletId, Arg.Is<Money>(m => m.Amount == -40m), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_DoesNotLookUpWallet_SoDateAndArchivalAreNotRechecked()
    {
        var operation = DeletedOperation(OperationEffectKind.Income, 70m, 70m, new DateOnly(2000, 1, 1));
        _operationRepository.Found(operation);

        await CreateHandler().Handle(new RestoreOperationCommand(operation.Id.Value), CancellationToken.None);

        await _walletBalanceGateway.DidNotReceive().GetAsync(Arg.Any<WalletId>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NotDeletedOperation_ThrowsOperationNotDeletedExceptionAndDoesNotTouchBalance()
    {
        var operation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
            Money(10m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(10m));
        _operationRepository.Found(operation);

        var act = () => CreateHandler().Handle(new RestoreOperationCommand(operation.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<OperationNotDeletedException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_TransferLeg_ThrowsOperationPartOfTransferExceptionAndDoesNotTouchBalance()
    {
        var (transfer, source, target) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));
        transfer.SoftDelete(source, target);
        _operationRepository.Found(source);

        var act = () => CreateHandler().Handle(new RestoreOperationCommand(source.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<OperationPartOfTransferException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        source.IsDeleted.Should().BeTrue();
    }
}
