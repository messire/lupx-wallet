using FluentAssertions;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// The balance correction behaviour of the Operation aggregate (ADR-0003, §6; ADR-0002, §3): created
/// from a stated balance, replaced only by a repeated statement, recalculated only by the system, never
/// edited, moved, soft-deleted or restored by the user. The "not editable" check comes first.
/// </summary>
public sealed class OperationCorrectionTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static readonly DateOnly Date = new(2026, 9, 10);

    private static Money Money(decimal amount) => new(amount, Currency);

    private static Operation NewCorrection(decimal stated = 350m, bool loadedFromDatabase = false)
    {
        var correction = Operation.CreateCorrection(OperationId.New(), WalletId.New(), Currency, Date, stated);
        if (loadedFromDatabase)
        {
            correction.ClearDomainEvents();
        }

        return correction;
    }

    // ---- CreateCorrection ----

    [Fact]
    public void CreateCorrection_SetsSystemRowShapeAndRaisesOperationCreated()
    {
        var walletId = WalletId.New();

        var correction = Operation.CreateCorrection(OperationId.New(), walletId, Currency, Date, 350m);

        correction.IsCorrection.Should().BeTrue();
        correction.OperationTypeId.Should().BeNull();
        correction.AdjustmentMode.Should().BeNull();
        correction.TransferId.Should().BeNull();
        correction.IsDeleted.Should().BeFalse();
        correction.StatedBalance.Should().Be(Money(350m));
        correction.Amount.Should().Be(Money(0m), "the amount starts at zero and is recalculated by the caller");
        correction.AppliedDelta.Should().Be(Money(0m));
        correction.WalletId.Should().Be(walletId);
        correction.OperationDate.Should().Be(Date);
        var raised = correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OperationCreated>().Subject;
        raised.OperationId.Should().Be(correction.Id);
        raised.WalletId.Should().Be(walletId);
        raised.OperationDate.Should().Be(Date);
    }

    [Fact]
    public void RegularOperation_IsNotACorrection()
    {
        var operation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
            Money(10m), Date, null, Money(10m));

        operation.IsCorrection.Should().BeFalse();
        operation.StatedBalance.Should().BeNull();
    }

    // ---- ReplaceStatedBalance ----

    [Fact]
    public void ReplaceStatedBalance_NewValue_ChangesItKeepsIdAndRaisesOperationUpdated()
    {
        var correction = NewCorrection(350m, loadedFromDatabase: true);
        var id = correction.Id;

        correction.ReplaceStatedBalance(Money(400m));

        correction.StatedBalance.Should().Be(Money(400m));
        correction.Id.Should().Be(id);
        var raised = correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OperationUpdated>().Subject;
        raised.OperationDate.Should().Be(Date);
        raised.PreviousOperationDate.Should().Be(Date);
        raised.WalletId.Should().Be(correction.WalletId);
        raised.PreviousWalletId.Should().Be(correction.WalletId);
    }

    [Fact]
    public void ReplaceStatedBalance_SameValue_ChangesNothingAndRaisesNoEvent()
    {
        var correction = NewCorrection(350m, loadedFromDatabase: true);
        var updatedAt = correction.UpdatedAt;

        correction.ReplaceStatedBalance(Money(350m));

        correction.DomainEvents.Should().BeEmpty();
        correction.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ReplaceStatedBalance_OtherCurrency_ThrowsOperationCurrencyImmutableException()
    {
        var correction = NewCorrection(350m, loadedFromDatabase: true);

        var act = () => correction.ReplaceStatedBalance(new Money(400m, new CurrencyId(Guid.NewGuid())));

        act.Should().Throw<OperationCurrencyImmutableException>();
        correction.StatedBalance.Should().Be(Money(350m));
    }

    [Fact]
    public void ReplaceStatedBalance_RegularOperation_Throws()
    {
        var operation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
            Money(10m), Date, null, Money(10m));

        var act = () => operation.ReplaceStatedBalance(Money(1m));

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- ApplyRecalculatedDelta ----

    [Fact]
    public void ApplyRecalculatedDelta_ChangedAmount_SetsAmountAndAppliedDeltaAndRaisesCorrectionRecalculated()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        correction.ApplyRecalculatedDelta(-150m);

        correction.Amount.Should().Be(Money(-150m));
        correction.AppliedDelta.Should().Be(Money(-150m));
        correction.StatedBalance.Should().Be(Money(350m), "the user's statement never changes");
        var raised = correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionRecalculated>().Subject;
        raised.OperationId.Should().Be(correction.Id);
        raised.WalletId.Should().Be(correction.WalletId);
        raised.OperationDate.Should().Be(Date);
    }

    [Fact]
    public void ApplyRecalculatedDelta_SameAmount_ChangesNothingAndRaisesNoEvent()
    {
        var correction = NewCorrection(loadedFromDatabase: true);
        correction.ApplyRecalculatedDelta(50m);
        correction.ClearDomainEvents();
        var updatedAt = correction.UpdatedAt;

        correction.ApplyRecalculatedDelta(50m);

        correction.DomainEvents.Should().BeEmpty();
        correction.UpdatedAt.Should().Be(updatedAt);
    }

    [Fact]
    public void ApplyRecalculatedDelta_ZeroOnAFreshZeroCorrection_RaisesNothing()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        correction.ApplyRecalculatedDelta(0m);

        correction.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void ApplyRecalculatedDelta_RightAfterCreation_KeepsASingleEventSoTheAuditHasOneEntry()
    {
        var correction = NewCorrection(loadedFromDatabase: false); // OperationCreated pending

        correction.ApplyRecalculatedDelta(50m);

        correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OperationCreated>();
        correction.Amount.Should().Be(Money(50m));
    }

    [Fact]
    public void ApplyRecalculatedDelta_RightAfterReplacement_KeepsASingleEvent()
    {
        var correction = NewCorrection(350m, loadedFromDatabase: true);
        correction.ReplaceStatedBalance(Money(400m));

        correction.ApplyRecalculatedDelta(75m);

        correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<OperationUpdated>();
    }

    [Fact]
    public void ApplyRecalculatedDelta_RegularOperation_Throws()
    {
        var operation = Operation.Create(
            OperationId.New(), WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income,
            Money(10m), Date, null, Money(10m));

        var act = () => operation.ApplyRecalculatedDelta(5m);

        act.Should().Throw<InvalidOperationException>();
    }

    // ---- Other system transitions ----

    [Fact]
    public void RebaseCurrency_NewCurrency_KeepsNumbersAndRaisesCorrectionCurrencyChanged()
    {
        var correction = NewCorrection(350m, loadedFromDatabase: true);
        correction.ApplyRecalculatedDelta(50m);
        correction.ClearDomainEvents();
        var newCurrency = new CurrencyId(Guid.NewGuid());

        correction.RebaseCurrency(newCurrency);

        correction.CurrencyId.Should().Be(newCurrency);
        correction.StatedBalance.Should().Be(new Money(350m, newCurrency));
        correction.Amount.Should().Be(new Money(50m, newCurrency));
        correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionCurrencyChanged>();
    }

    [Fact]
    public void RebaseCurrency_SameCurrency_DoesNothing()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        correction.RebaseCurrency(Currency);

        correction.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void MarkRemovedWithWallet_RaisesCorrectionRemovedAndDoesNotSoftDelete()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        correction.MarkRemovedWithWallet();

        correction.IsDeleted.Should().BeFalse("a correction is removed physically, never soft-deleted");
        var raised = correction.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<CorrectionRemoved>().Subject;
        raised.OperationId.Should().Be(correction.Id);
        raised.OperationDate.Should().Be(Date);
    }

    // ---- Forbidden user actions: CorrectionNotEditableException first ----

    [Fact]
    public void Update_Correction_ThrowsCorrectionNotEditableAndKeepsState()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        var act = () => correction.Update(
            OperationTypeId.New(), OperationEffectKind.Income, Money(10m), Date, null, Money(10m));

        act.Should().Throw<CorrectionNotEditableException>();
        correction.OperationTypeId.Should().BeNull();
        correction.Amount.Should().Be(Money(0m));
        correction.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Update_Correction_WithAnInvalidInput_StillReportsCorrectionNotEditableFirst()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        // Transfer behaviour and a currency change would fail other checks; the correction check precedes them.
        var act = () => correction.Update(
            OperationTypeId.New(), OperationEffectKind.Transfer, new Money(-5m, new CurrencyId(Guid.NewGuid())), Date, null, Money(1m));

        act.Should().Throw<CorrectionNotEditableException>();
    }

    [Fact]
    public void MoveToWallet_Correction_ThrowsCorrectionNotEditableAndKeepsWallet()
    {
        var correction = NewCorrection(loadedFromDatabase: true);
        var walletId = correction.WalletId;

        var act = () => correction.MoveToWallet(
            WalletId.New(), OperationTypeId.New(), OperationEffectKind.Income, Money(10m), Date, null, Money(10m));

        act.Should().Throw<CorrectionNotEditableException>();
        correction.WalletId.Should().Be(walletId);
        correction.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void SoftDelete_Correction_ThrowsCorrectionNotEditableAndStaysActive()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        var act = correction.SoftDelete;

        act.Should().Throw<CorrectionNotEditableException>();
        correction.IsDeleted.Should().BeFalse();
        correction.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Restore_Correction_ThrowsCorrectionNotEditableNotOperationNotDeleted()
    {
        var correction = NewCorrection(loadedFromDatabase: true);

        var act = correction.Restore;

        act.Should().Throw<CorrectionNotEditableException>("a correction is never deleted, but the answer must say that it is system-calculated");
    }

    [Fact]
    public void EnsureEditable_Correction_Throws()
    {
        var act = () => NewCorrection().EnsureEditable();

        act.Should().Throw<CorrectionNotEditableException>();
    }

    [Fact]
    public void CorrectionNotEditableMessage_HasNoIdentifiers()
    {
        var correction = NewCorrection();

        var exception = new CorrectionNotEditableException(correction.Id.Value);

        exception.Message.Should().NotContain(correction.Id.Value.ToString());
        exception.Message.Should().NotMatchRegex("[0-9a-f]{8}-[0-9a-f]{4}-");
        exception.Message.Should().Contain("корректирующая сумма");
    }

    // ---- Absolute is gone ----

    [Fact]
    public void AdjustmentMode_HasOnlyDelta()
    {
        Enum.GetNames<AdjustmentMode>().Should().Equal("Delta");
        Enum.TryParse<AdjustmentMode>("Absolute", ignoreCase: true, out _).Should().BeFalse();
    }

    [Fact]
    public void AbsoluteAdjustmentMessage_PointsToTheStatedBalanceActionWithoutIdentifiers()
    {
        new AbsoluteAdjustmentNotSupportedException().Message.Should().Contain("Указать баланс на дату");
    }
}
