using FluentAssertions;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// Агрегат Operation (ddd-model.md, §2.4, §5): создание Income/Expense/Adjustment,
/// защита операций — частей перевода, неизменяемость валюты при редактировании.
/// </summary>
public sealed class OperationTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    private static Operation CreateIncome(decimal amount = 100m, DateOnly? date = null) => Operation.Create(
        OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
        Money(amount), date ?? DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(amount));

    [Fact]
    public void Create_Income_WithPositiveAmount_Succeeds()
    {
        var operation = CreateIncome(100m);

        operation.Amount.Amount.Should().Be(100m);
        operation.AppliedDelta.Amount.Should().Be(100m);
        operation.AdjustmentMode.Should().BeNull();
        operation.TransferId.Should().BeNull();
    }

    [Theory]
    [InlineData(OperationEffectKind.Income)]
    [InlineData(OperationEffectKind.Expense)]
    public void Create_IncomeOrExpense_WithNonPositiveAmount_ThrowsOperationAmountMustBePositiveException(OperationEffectKind kind)
    {
        var act = () => Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), kind,
            Money(0m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(0m));

        act.Should().Throw<OperationAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_Adjustment_WithoutMode_ThrowsAdjustmentModeRequiredException()
    {
        var act = () => Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Adjustment,
            Money(10m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(10m));

        act.Should().Throw<AdjustmentModeRequiredException>();
    }

    [Theory]
    [InlineData(OperationEffectKind.Income)]
    [InlineData(OperationEffectKind.Expense)]
    public void Create_NonAdjustment_WithModeSupplied_ThrowsAdjustmentModeNotAllowedException(OperationEffectKind kind)
    {
        var act = () => Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), kind,
            Money(10m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: AdjustmentMode.Delta, appliedDelta: Money(10m));

        act.Should().Throw<AdjustmentModeNotAllowedException>();
    }

    [Fact]
    public void Create_AdjustmentWithMode_DoesNotRequirePositiveAmount()
    {
        // Корректировка дельтой может быть отрицательной — Adjustment не подпадает под
        // требование "сумма положительна" (OperationEffectCalculator).
        var act = () => Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Adjustment,
            Money(-50m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: AdjustmentMode.Delta, appliedDelta: Money(-50m));

        act.Should().NotThrow();
    }

    [Fact]
    public void Create_TransferBehaviorWithoutTransferId_ThrowsDirectOperationCreationNotAllowedForBehaviorException()
    {
        var act = () => Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Transfer,
            Money(10m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(10m), transferId: null);

        act.Should().Throw<DirectOperationCreationNotAllowedForBehaviorException>();
    }

    [Fact]
    public void Update_OperationPartOfTransfer_ThrowsOperationPartOfTransferException()
    {
        var transferOperation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
            Money(10m), DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(10m));
        // Симулируем часть перевода через CreateForTransfer (internal) недоступен напрямую тестам
        // другой сборки — используем Transfer.Create, которое его вызывает (см. TransferTests).
        var (_, sourceOperation, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => sourceOperation.Update(
            OperationTypeId.New(), OperationEffectKind.Income, Money(20m),
            DateOnly.FromDateTime(DateTime.UtcNow), adjustmentMode: null, appliedDelta: Money(20m));

        act.Should().Throw<OperationPartOfTransferException>();
    }

    [Fact]
    public void Update_ChangingCurrency_ThrowsOperationCurrencyImmutableException()
    {
        var operation = CreateIncome(100m);
        var otherCurrency = new Money(100m, new CurrencyId(Guid.NewGuid()));

        var act = () => operation.Update(
            operation.OperationTypeId!.Value, OperationEffectKind.Income, otherCurrency,
            operation.OperationDate, adjustmentMode: null, appliedDelta: otherCurrency);

        act.Should().Throw<OperationCurrencyImmutableException>();
    }

    [Fact]
    public void Update_ValidChange_UpdatesFieldsAndRaisesOperationUpdatedWithPreviousDate()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 1, 10));
        var newDate = new DateOnly(2026, 1, 5);
        var newTypeId = OperationTypeId.New();

        operation.Update(newTypeId, OperationEffectKind.Income, Money(150m), newDate, adjustmentMode: null, appliedDelta: Money(150m));

        operation.Amount.Amount.Should().Be(150m);
        operation.OperationDate.Should().Be(newDate);
        operation.OperationTypeId.Should().Be(newTypeId);
        var updatedEvent = operation.DomainEvents.OfType<OperationUpdated>().Single();
        updatedEvent.OperationDate.Should().Be(newDate);
        updatedEvent.PreviousOperationDate.Should().Be(new DateOnly(2026, 1, 10));
    }

    [Fact]
    public void SoftDelete_OperationPartOfTransfer_ThrowsOperationPartOfTransferException()
    {
        var (_, sourceOperation, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => sourceOperation.SoftDelete();

        act.Should().Throw<OperationPartOfTransferException>();
    }

    [Fact]
    public void SoftDelete_StandaloneOperation_SetsDeletedAtWithoutTouchingUpdatedAt()
    {
        var operation = CreateIncome();
        var updatedAt = operation.UpdatedAt;

        operation.SoftDelete();

        operation.IsDeleted.Should().BeTrue();
        operation.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void SoftDelete_AlreadyDeleted_ThrowsOperationAlreadyDeletedException()
    {
        var operation = CreateIncome();
        operation.SoftDelete();

        var act = () => operation.SoftDelete();

        act.Should().Throw<OperationAlreadyDeletedException>();
    }

    // ---- MoveToWallet (UC-13, решение пользователя от 2026-09-11: перенос операции на другой кошелек) ----

    [Fact]
    public void MoveToWallet_OperationPartOfTransfer_ThrowsOperationPartOfTransferException()
    {
        var (_, sourceOperation, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => sourceOperation.MoveToWallet(
            WalletId.New(), sourceOperation.OperationTypeId!.Value, OperationEffectKind.Income, Money(10m),
            sourceOperation.OperationDate, adjustmentMode: null, appliedDelta: Money(10m));

        act.Should().Throw<OperationPartOfTransferException>();
    }

    [Fact]
    public void MoveToWallet_DifferentCurrencyThanBefore_DoesNotThrowAndAdoptsNewCurrency()
    {
        // В отличие от Update, MoveToWallet не проверяет неизменность валюты — валюта
        // операции меняется вслед за валютой нового кошелька (как при первичном Create).
        var operation = CreateIncome(100m, new DateOnly(2026, 1, 10));
        var newCurrency = new CurrencyId(Guid.NewGuid());
        var newAmount = new Money(100m, newCurrency);
        var newWalletId = WalletId.New();

        var act = () => operation.MoveToWallet(
            newWalletId, operation.OperationTypeId!.Value, OperationEffectKind.Income, newAmount,
            operation.OperationDate, adjustmentMode: null, appliedDelta: newAmount);

        act.Should().NotThrow();
        operation.WalletId.Should().Be(newWalletId);
        operation.CurrencyId.Should().Be(newCurrency);
        operation.Amount.Amount.Should().Be(100m);
    }

    [Fact]
    public void MoveToWallet_ValidChange_UpdatesWalletIdAndRaisesOperationUpdatedWithPreviousWalletId()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 1, 10));
        var previousWalletId = operation.WalletId;
        var newWalletId = WalletId.New();
        var newDate = new DateOnly(2026, 1, 5);
        var newAmount = Money(150m);

        operation.MoveToWallet(
            newWalletId, operation.OperationTypeId!.Value, OperationEffectKind.Income, newAmount,
            newDate, adjustmentMode: null, appliedDelta: newAmount);

        operation.WalletId.Should().Be(newWalletId);
        operation.OperationDate.Should().Be(newDate);
        operation.AppliedDelta.Amount.Should().Be(150m);

        var updatedEvent = operation.DomainEvents.OfType<OperationUpdated>().Single();
        updatedEvent.WalletId.Should().Be(newWalletId);
        updatedEvent.PreviousWalletId.Should().Be(previousWalletId);
        updatedEvent.OperationDate.Should().Be(newDate);
        updatedEvent.PreviousOperationDate.Should().Be(new DateOnly(2026, 1, 10));
    }

    [Fact]
    public void Update_SameWallet_RaisesOperationUpdatedWithPreviousWalletIdEqualToCurrent()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 1, 10));

        operation.Update(
            operation.OperationTypeId!.Value, OperationEffectKind.Income, Money(120m),
            operation.OperationDate, adjustmentMode: null, appliedDelta: Money(120m));

        var updatedEvent = operation.DomainEvents.OfType<OperationUpdated>().Single();
        updatedEvent.PreviousWalletId.Should().Be(operation.WalletId);
        updatedEvent.WalletId.Should().Be(operation.WalletId);
    }

    [Fact]
    public void SoftDelete_RaisesOperationDeletedEvent()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 3, 1));

        operation.SoftDelete();

        var deletedEvent = operation.DomainEvents.OfType<OperationDeleted>().Single();
        deletedEvent.WalletId.Should().Be(operation.WalletId);
        deletedEvent.OperationDate.Should().Be(new DateOnly(2026, 3, 1));
    }

    // ---- Soft delete / restore (ADR-0002, §3) ----

    [Fact]
    public void SoftDelete_StandaloneOperation_SetsDeletedAtAndRaisesEventWithOperationId()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 3, 1));
        var before = DateTimeOffset.UtcNow;

        operation.SoftDelete();

        operation.IsDeleted.Should().BeTrue();
        operation.DeletedAt.Should().NotBeNull().And.BeOnOrAfter(before);
        operation.DomainEvents.OfType<OperationDeleted>().Single().OperationId.Should().Be(operation.Id);
    }

    [Fact]
    public void SoftDelete_AlreadyDeleted_DoesNotChangeDeletedAtOrRaiseSecondEvent()
    {
        var operation = CreateIncome();
        operation.SoftDelete();
        var deletedAt = operation.DeletedAt;

        var act = () => operation.SoftDelete();

        act.Should().Throw<OperationAlreadyDeletedException>();
        operation.DeletedAt.Should().Be(deletedAt);
        operation.DomainEvents.OfType<OperationDeleted>().Should().ContainSingle();
    }

    [Fact]
    public void Restore_DeletedOperation_ClearsDeletedAtAndRaisesOperationRestoredWithDate()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 3, 1));
        operation.SoftDelete();

        operation.Restore();

        operation.IsDeleted.Should().BeFalse();
        operation.DeletedAt.Should().BeNull();
        var restored = operation.DomainEvents.OfType<OperationRestored>().Single();
        restored.OperationId.Should().Be(operation.Id);
        restored.WalletId.Should().Be(operation.WalletId);
        restored.OperationDate.Should().Be(new DateOnly(2026, 3, 1));
    }

    [Fact]
    public void Restore_DoesNotTouchUpdatedAtNorAppliedDelta()
    {
        var operation = CreateIncome(100m);
        var updatedAt = operation.UpdatedAt;
        operation.SoftDelete();

        operation.Restore();

        operation.UpdatedAt.Should().Be(updatedAt, "UpdatedAt is the moment of a content edit; delete/restore moments live in DeletedAt and audit");
        operation.AppliedDelta.Amount.Should().Be(100m);
    }

    [Fact]
    public void Restore_NotDeletedOperation_ThrowsOperationNotDeletedExceptionAndRaisesNothing()
    {
        var operation = CreateIncome();

        var act = () => operation.Restore();

        act.Should().Throw<OperationNotDeletedException>();
        operation.DomainEvents.OfType<OperationRestored>().Should().BeEmpty();
    }

    [Fact]
    public void Restore_OperationPartOfTransfer_ThrowsOperationPartOfTransferException()
    {
        var (_, sourceOperation, _) = Transfer.Create(
            TransferId.New(), WalletId.New(), WalletId.New(), OperationTypeId.New(), Money(10m), DateOnly.FromDateTime(DateTime.UtcNow));

        var act = () => sourceOperation.Restore();

        act.Should().Throw<OperationPartOfTransferException>();
    }

    [Fact]
    public void SoftDeleteThenRestoreThenSoftDelete_IsAllowed()
    {
        var operation = CreateIncome();

        operation.SoftDelete();
        operation.Restore();
        var act = () => operation.SoftDelete();

        act.Should().NotThrow();
        operation.IsDeleted.Should().BeTrue();
    }

    [Fact]
    public void Update_DeletedOperation_ThrowsOperationIsDeletedExceptionAndKeepsState()
    {
        var operation = CreateIncome(100m, new DateOnly(2026, 1, 10));
        operation.SoftDelete();
        var updatedAt = operation.UpdatedAt;

        var act = () => operation.Update(
            OperationTypeId.New(), OperationEffectKind.Income, Money(999m),
            new DateOnly(2026, 1, 5), adjustmentMode: null, appliedDelta: Money(999m));

        act.Should().Throw<OperationIsDeletedException>();
        operation.Amount.Amount.Should().Be(100m);
        operation.OperationDate.Should().Be(new DateOnly(2026, 1, 10));
        operation.UpdatedAt.Should().Be(updatedAt);
        operation.DomainEvents.OfType<OperationUpdated>().Should().BeEmpty();
    }

    [Fact]
    public void MoveToWallet_DeletedOperation_ThrowsOperationIsDeletedExceptionAndKeepsWallet()
    {
        var operation = CreateIncome(100m);
        var walletId = operation.WalletId;
        operation.SoftDelete();

        var act = () => operation.MoveToWallet(
            WalletId.New(), operation.OperationTypeId!.Value, OperationEffectKind.Income, Money(100m),
            operation.OperationDate, adjustmentMode: null, appliedDelta: Money(100m));

        act.Should().Throw<OperationIsDeletedException>();
        operation.WalletId.Should().Be(walletId);
        operation.DomainEvents.OfType<OperationUpdated>().Should().BeEmpty();
    }

    [Fact]
    public void Update_AfterRestore_IsAllowedAgain()
    {
        var operation = CreateIncome(100m);
        operation.SoftDelete();
        operation.Restore();

        var act = () => operation.Update(
            operation.OperationTypeId!.Value, OperationEffectKind.Income, Money(150m),
            operation.OperationDate, adjustmentMode: null, appliedDelta: Money(150m));

        act.Should().NotThrow();
        operation.Amount.Amount.Should().Be(150m);
    }

    [Fact]
    public void EnsureNotDeleted_ActiveOperation_DoesNotThrow()
    {
        var operation = CreateIncome();

        var act = () => operation.EnsureNotDeleted();

        act.Should().NotThrow();
    }
}
