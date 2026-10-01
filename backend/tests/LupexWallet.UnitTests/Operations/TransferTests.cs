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
    public void SoftDelete_MarksTransferAndBothLegsWithSameDeletedAt()
    {
        var (transfer, source, target) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        transfer.SoftDelete(source, target);

        transfer.IsDeleted.Should().BeTrue();
        source.DeletedAt.Should().Be(transfer.DeletedAt);
        target.DeletedAt.Should().Be(transfer.DeletedAt);
        transfer.DomainEvents.OfType<TransferDeleted>().Should().ContainSingle();
    }

    [Fact]
    public void Restore_AfterSoftDelete_ClearsDeletedAtOnAllThree()
    {
        var (transfer, source, target) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));
        transfer.SoftDelete(source, target);

        transfer.Restore(source, target);

        transfer.IsDeleted.Should().BeFalse();
        source.IsDeleted.Should().BeFalse();
        target.IsDeleted.Should().BeFalse();
        transfer.DomainEvents.OfType<TransferRestored>().Should().ContainSingle();
    }

    private static (Transfer Transfer, Operation Source, Operation Target) CreateTransfer(DateOnly? date = null) =>
        Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m),
            date ?? DateOnly.FromDateTime(DateTime.UtcNow));

    [Fact]
    public void SoftDelete_RaisesTransferDeletedAndOperationDeletedForEachLegWithItsWalletAndDate()
    {
        var date = new DateOnly(2026, 4, 2);
        var (transfer, source, target) = CreateTransfer(date);

        transfer.SoftDelete(source, target);

        transfer.DomainEvents.OfType<TransferDeleted>().Single().TransferId.Should().Be(transfer.Id);
        var legEvents = source.DomainEvents.OfType<OperationDeleted>().Concat(target.DomainEvents.OfType<OperationDeleted>()).ToList();
        legEvents.Should().HaveCount(2, "BalanceHistory recalculates both wallets through the leg events");
        legEvents.Select(e => e.WalletId).Should().BeEquivalentTo([transfer.SourceWalletId, transfer.TargetWalletId]);
        legEvents.Should().OnlyContain(e => e.OperationDate == date);
    }

    [Fact]
    public void SoftDelete_DoesNotChangeLegAppliedDeltas()
    {
        var (transfer, source, target) = CreateTransfer();

        transfer.SoftDelete(source, target);

        source.AppliedDelta.Amount.Should().Be(-10m);
        target.AppliedDelta.Amount.Should().Be(10m);
    }

    [Fact]
    public void SoftDelete_AlreadyDeleted_ThrowsTransferAlreadyDeletedExceptionAndKeepsDeletedAt()
    {
        var (transfer, source, target) = CreateTransfer();
        transfer.SoftDelete(source, target);
        var deletedAt = transfer.DeletedAt;

        var act = () => transfer.SoftDelete(source, target);

        act.Should().Throw<TransferAlreadyDeletedException>();
        transfer.DeletedAt.Should().Be(deletedAt);
        source.DeletedAt.Should().Be(deletedAt);
        transfer.DomainEvents.OfType<TransferDeleted>().Should().ContainSingle();
    }

    [Fact]
    public void SoftDelete_ForeignLegs_ThrowsTransferLegMismatchExceptionAndChangesNothing()
    {
        var (transfer, source, target) = CreateTransfer();
        var (_, foreignSource, foreignTarget) = CreateTransfer();

        var act = () => transfer.SoftDelete(foreignSource, foreignTarget);

        act.Should().Throw<TransferLegMismatchException>();
        transfer.IsDeleted.Should().BeFalse();
        foreignSource.IsDeleted.Should().BeFalse();
        foreignTarget.IsDeleted.Should().BeFalse();
        source.IsDeleted.Should().BeFalse();
        target.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void SoftDelete_LegsPassedInSwappedOrder_ThrowsTransferLegMismatchException()
    {
        var (transfer, source, target) = CreateTransfer();

        var act = () => transfer.SoftDelete(target, source);

        act.Should().Throw<TransferLegMismatchException>();
        transfer.IsDeleted.Should().BeFalse();
    }

    [Fact]
    public void Restore_RaisesTransferRestoredAndOperationRestoredForEachLeg()
    {
        var date = new DateOnly(2026, 4, 2);
        var (transfer, source, target) = CreateTransfer(date);
        transfer.SoftDelete(source, target);

        transfer.Restore(source, target);

        transfer.DomainEvents.OfType<TransferRestored>().Single().TransferId.Should().Be(transfer.Id);
        var legEvents = source.DomainEvents.OfType<OperationRestored>().Concat(target.DomainEvents.OfType<OperationRestored>()).ToList();
        legEvents.Should().HaveCount(2);
        legEvents.Select(e => e.WalletId).Should().BeEquivalentTo([transfer.SourceWalletId, transfer.TargetWalletId]);
        legEvents.Should().OnlyContain(e => e.OperationDate == date);
        transfer.DeletedAt.Should().BeNull();
    }

    [Fact]
    public void Restore_NotDeleted_ThrowsTransferNotDeletedException()
    {
        var (transfer, source, target) = CreateTransfer();

        var act = () => transfer.Restore(source, target);

        act.Should().Throw<TransferNotDeletedException>();
        transfer.DomainEvents.OfType<TransferRestored>().Should().BeEmpty();
    }

    [Fact]
    public void Restore_ForeignLegs_ThrowsTransferLegMismatchExceptionAndKeepsDeletion()
    {
        var (transfer, source, target) = CreateTransfer();
        transfer.SoftDelete(source, target);
        var (_, foreignSource, foreignTarget) = CreateTransfer();

        var act = () => transfer.Restore(foreignSource, foreignTarget);

        act.Should().Throw<TransferLegMismatchException>();
        transfer.IsDeleted.Should().BeTrue();
        source.IsDeleted.Should().BeTrue();
        target.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void SoftDeleteThenRestoreThenSoftDelete_IsAllowedAndLegsShareDeletedAt()
    {
        var (transfer, source, target) = CreateTransfer();

        transfer.SoftDelete(source, target);
        transfer.Restore(source, target);
        transfer.DeletedAt.Should().BeNull();
        var act = () => transfer.SoftDelete(source, target);

        act.Should().NotThrow();
        transfer.IsDeleted.Should().BeTrue();
        source.DeletedAt.Should().Be(transfer.DeletedAt);
        target.DeletedAt.Should().Be(transfer.DeletedAt);
    }
}
