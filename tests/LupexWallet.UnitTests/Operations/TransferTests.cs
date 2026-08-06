using FluentAssertions;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// Агрегат Transfer (ddd-model.md, §2.5, §5): атомарная пара операций, защита от смешения
/// кошельков, равенство сумм списания/зачисления (решено, Q10 — без комиссии).
/// </summary>
public sealed class TransferTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    [Fact]
    public void Create_SameSourceAndTargetWallet_ThrowsTransferSameWalletException()
    {
        var walletId = WalletId.New();

        var act = () => Transfer.Create(
            TransferId.New(), walletId, walletId, OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        act.Should().Throw<TransferSameWalletException>();
    }

    [Fact]
    public void Create_NonPositiveAmount_ThrowsOperationAmountMustBePositiveException()
    {
        var act = () => Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(0m), DateOnly.FromDateTime(DateTime.UtcNow));

        act.Should().Throw<OperationAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_Valid_ProducesTransferAndTwoOppositeOperations()
    {
        var sourceWalletId = WalletId.New();
        var targetWalletId = WalletId.New();
        var date = new DateOnly(2026, 5, 1);

        var (transfer, sourceOperation, targetOperation) = Transfer.Create(
            TransferId.New(), sourceWalletId, targetWalletId, OperationTypeId.New(), Money(75m), date);

        transfer.SourceWalletId.Should().Be(sourceWalletId);
        transfer.TargetWalletId.Should().Be(targetWalletId);
        transfer.Amount.Amount.Should().Be(75m);
        transfer.SourceOperationId.Should().Be(sourceOperation.Id);
        transfer.TargetOperationId.Should().Be(targetOperation.Id);

        sourceOperation.WalletId.Should().Be(sourceWalletId);
        sourceOperation.AppliedDelta.Amount.Should().Be(-75m, "списание уменьшает баланс исходного кошелька");
        sourceOperation.TransferId.Should().Be(transfer.Id);
        sourceOperation.AdjustmentMode.Should().BeNull();

        targetOperation.WalletId.Should().Be(targetWalletId);
        targetOperation.AppliedDelta.Amount.Should().Be(75m, "зачисление увеличивает баланс целевого кошелька");
        targetOperation.TransferId.Should().Be(transfer.Id);

        sourceOperation.Amount.Amount.Should().Be(targetOperation.Amount.Amount, "Q10: сумма списания равна сумме зачисления, комиссия не поддерживается");
    }

    [Fact]
    public void Create_RaisesTransferCreatedEvent()
    {
        var (transfer, _, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var raised = transfer.DomainEvents.OfType<TransferCreated>().Single();
        raised.TransferId.Should().Be(transfer.Id);
    }

    [Fact]
    public void EnsureCanBeDeleted_WithHistory_ThrowsTransferDeletionNotAllowedException()
    {
        var (transfer, _, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => transfer.EnsureCanBeDeleted(hasHistory: true);

        act.Should().Throw<TransferDeletionNotAllowedException>();
    }

    [Fact]
    public void EnsureCanBeDeleted_WithoutHistory_DoesNotThrow()
    {
        var (transfer, _, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => transfer.EnsureCanBeDeleted(hasHistory: false);

        act.Should().NotThrow();
    }
}
