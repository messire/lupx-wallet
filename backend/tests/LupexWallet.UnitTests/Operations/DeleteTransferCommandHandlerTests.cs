using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// DeleteTransferCommandHandler — атомарное удаление перевода: реверс дельт обоих кошельков
/// и мягкое удаление обеих операций вместе с Transfer (ddd-model.md, §5: "Transfer не может
/// существовать без обеих связанных Operation").
/// </summary>
public sealed class DeleteTransferCommandHandlerTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    private readonly ITransferRepository _transferRepository = Substitute.For<ITransferRepository>();
    private readonly IOperationRepository _operationRepository = Substitute.For<IOperationRepository>();
    private readonly IOperationsUnitOfWork _unitOfWork = Substitute.For<IOperationsUnitOfWork>();
    private readonly IWalletBalanceGateway _walletBalanceGateway = Substitute.For<IWalletBalanceGateway>();

    private DeleteTransferCommandHandler CreateHandler() =>
        new(_transferRepository, _operationRepository, _unitOfWork, _walletBalanceGateway);

    [Fact]
    public async Task Handle_UnknownTransfer_ThrowsKeyNotFoundException()
    {
        _transferRepository.GetByIdAsync(Arg.Any<TransferId>(), Arg.Any<CancellationToken>()).Returns((Transfer?)null);
        var command = new DeleteTransferCommand(Guid.NewGuid());

        var act = () => CreateHandler().Handle(command, CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task Handle_TransferDatedBeforeToday_SoftDeletesAndReversesBothDeltas()
    {
        // ADR-0002, §3: прежнее правило «граница — сегодня» отменено, удаление допустимо с любой датой.
        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var (transfer, sourceOperation, targetOperation) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(50m), yesterday);
        _transferRepository.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);
        _operationRepository.GetByIdAsync(transfer.SourceOperationId, Arg.Any<CancellationToken>()).Returns(sourceOperation);
        _operationRepository.GetByIdAsync(transfer.TargetOperationId, Arg.Any<CancellationToken>()).Returns(targetOperation);
        var command = new DeleteTransferCommand(transfer.Id.Value);

        await CreateHandler().Handle(command, CancellationToken.None);

        transfer.IsDeleted.Should().BeTrue();
        await _walletBalanceGateway.Received(2).ApplyDeltaAsync(Arg.Any<WalletId>(), Arg.Any<Money>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_Valid_ReversesBothDeltasAndSoftDeletesTransferAndBothOperations()
    {
        var (transfer, sourceOperation, targetOperation) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(50m), DateOnly.FromDateTime(DateTime.UtcNow));
        _transferRepository.GetByIdAsync(transfer.Id, Arg.Any<CancellationToken>()).Returns(transfer);
        _operationRepository.GetByIdAsync(transfer.SourceOperationId, Arg.Any<CancellationToken>()).Returns(sourceOperation);
        _operationRepository.GetByIdAsync(transfer.TargetOperationId, Arg.Any<CancellationToken>()).Returns(targetOperation);
        var command = new DeleteTransferCommand(transfer.Id.Value);

        await CreateHandler().Handle(command, CancellationToken.None);

        // sourceOperation.AppliedDelta = -50 -> реверс +50; targetOperation.AppliedDelta = +50 -> реверс -50.
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            transfer.SourceWalletId, Arg.Is<Money>(m => m.Amount == 50m), Arg.Any<CancellationToken>());
        await _walletBalanceGateway.Received(1).ApplyDeltaAsync(
            transfer.TargetWalletId, Arg.Is<Money>(m => m.Amount == -50m), Arg.Any<CancellationToken>());
        transfer.IsDeleted.Should().BeTrue();
        sourceOperation.DeletedAt.Should().Be(transfer.DeletedAt);
        targetOperation.DeletedAt.Should().Be(transfer.DeletedAt);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }
}
