using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// RestoreTransferCommandHandler (ADR-0002, §3, §6) — restores the transfer and both legs as
/// a whole and re-applies each leg's AppliedDelta on its own wallet (source: debit, target: credit).
/// </summary>
public sealed class RestoreTransferCommandHandlerTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    private readonly ITransferRepository _transferRepository = Substitute.For<ITransferRepository>();
    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();
    private readonly IWalletCorrectionsReconciler _correctionsReconciler = Substitute.For<IWalletCorrectionsReconciler>();

    private RestoreTransferCommandHandler CreateHandler() =>
        new(_transferRepository, _operationRepository, _unitOfWork, _walletBalanceGateway, _correctionsReconciler);

    private (Transfer Transfer, Operation Source, Operation Target) ArrangeTransfer(bool deleted)
    {
        var (transfer, source, target) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(50m),
            DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-3));
        if (deleted)
        {
            transfer.SoftDelete(source, target);
        }

        _transferRepository.Found(transfer);
        _operationRepository.GetByIdAsync(transfer.SourceOperationId, Arg.Any<CancellationToken>()).Returns(source);
        _operationRepository.GetByIdAsync(transfer.TargetOperationId, Arg.Any<CancellationToken>()).Returns(target);
        return (transfer, source, target);
    }

    [Fact]
    public void Command_IsMarkedAsCommand_SoThatTransactionBehaviorApplies()
    {
        new RestoreTransferCommand(Guid.NewGuid()).Should().BeAssignableTo<ICommand<TransferDto>>();
    }

    [Fact]
    public async Task Handle_UnknownTransfer_ThrowsKeyNotFoundException()
    {
        _transferRepository.GetByIdAsync(Arg.Any<TransferId>(), Arg.Any<CancellationToken>()).Returns((Transfer?)null);

        var act = () => CreateHandler().Handle(new RestoreTransferCommand(Guid.NewGuid()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_DeletedTransfer_ReappliesBothDeltasOnTheirOwnWalletsAndSavesOnce()
    {
        var (transfer, source, target) = ArrangeTransfer(deleted: true);

        var result = await CreateHandler().Handle(new RestoreTransferCommand(transfer.Id.Value), CancellationToken.None);

        // Source leg AppliedDelta = -50 (debit), target leg = +50 (credit): same signs as at creation.
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            transfer.SourceWalletId, Arg.Is<Money>(m => m.Amount == -50m), Arg.Any<CancellationToken>());
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            transfer.TargetWalletId, Arg.Is<Money>(m => m.Amount == 50m), Arg.Any<CancellationToken>());
        await _walletBalanceGateway.Received(2).ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        transfer.IsDeleted.Should().BeFalse();
        source.IsDeleted.Should().BeFalse();
        target.IsDeleted.Should().BeFalse();
        result.Id.Should().Be(transfer.Id.Value);
        result.IsDeleted.Should().BeFalse();
        result.DeletedAt.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NotDeletedTransfer_ThrowsTransferNotDeletedExceptionAndDoesNotTouchAnyWallet()
    {
        var (transfer, _, _) = ArrangeTransfer(deleted: false);

        var act = () => CreateHandler().Handle(new RestoreTransferCommand(transfer.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<TransferNotDeletedException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
        await _unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingLeg_ThrowsKeyNotFoundExceptionAndDoesNotTouchAnyWallet()
    {
        var (transfer, _, _) = ArrangeTransfer(deleted: true);
        _operationRepository.GetByIdAsync(transfer.TargetOperationId, Arg.Any<CancellationToken>()).Returns((Operation?)null);

        var act = () => CreateHandler().Handle(new RestoreTransferCommand(transfer.Id.Value), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
        await _walletBalanceGateway.DidNotReceive().ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }
}
