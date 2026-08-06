using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// OperationEffectCalculator — вычисление знаковой дельты для Income/Expense/Adjustment
/// (оба режима: Absolute/Delta, решено Q3) и защита от прямого создания операции с
/// поведением Transfer (регресс: раньше падало обычным InvalidOperationException → 500,
/// docs/PROGRESS.md, "Баги, найденные и исправленные").
/// </summary>
public sealed class OperationEffectCalculatorTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());
    private static Money Money(decimal amount) => new(amount, Currency);

    [Fact]
    public void ComputeIntendedDelta_Income_ReturnsAmountAsIs()
    {
        var delta = OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Income, Money(100m), null, Money(0m));

        delta.Amount.Should().Be(100m);
    }

    [Fact]
    public void ComputeIntendedDelta_Expense_ReturnsNegatedAmount()
    {
        var delta = OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Expense, Money(40m), null, Money(0m));

        delta.Amount.Should().Be(-40m);
    }

    [Fact]
    public void ComputeIntendedDelta_AdjustmentDelta_ReturnsAmountAsIs()
    {
        var delta = OperationEffectCalculator.ComputeIntendedDelta(
            OperationEffectKind.Adjustment, Money(-15m), AdjustmentMode.Delta, Money(200m));

        delta.Amount.Should().Be(-15m);
    }

    [Fact]
    public void ComputeIntendedDelta_AdjustmentAbsolute_ReturnsNewValueMinusBaseline()
    {
        // Пользователь хочет, чтобы баланс стал равен 500, при текущем базовом (baseline) 350.
        var delta = OperationEffectCalculator.ComputeIntendedDelta(
            OperationEffectKind.Adjustment, Money(500m), AdjustmentMode.Absolute, Money(350m));

        delta.Amount.Should().Be(150m);
    }

    [Fact]
    public void ComputeIntendedDelta_Transfer_ThrowsDirectOperationCreationNotAllowedForBehaviorException()
    {
        var act = () => OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Transfer, Money(10m), null, Money(0m));

        act.Should().Throw<DirectOperationCreationNotAllowedForBehaviorException>();
    }

    [Fact]
    public void ComputeIntendedDelta_AdjustmentWithoutMode_ThrowsAdjustmentModeRequiredException()
    {
        var act = () => OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Adjustment, Money(10m), null, Money(0m));

        act.Should().Throw<AdjustmentModeRequiredException>();
    }

    [Theory]
    [InlineData(OperationEffectKind.Income)]
    [InlineData(OperationEffectKind.Expense)]
    public void ComputeIntendedDelta_NonAdjustmentWithMode_ThrowsAdjustmentModeNotAllowedException(OperationEffectKind kind)
    {
        var act = () => OperationEffectCalculator.ComputeIntendedDelta(kind, Money(10m), AdjustmentMode.Delta, Money(0m));

        act.Should().Throw<AdjustmentModeNotAllowedException>();
    }

    [Fact]
    public void ComputeBaseline_SubtractsPreviouslyAppliedDeltaFromCurrentBalance()
    {
        var baseline = OperationEffectCalculator.ComputeBaseline(Money(300m), Money(50m));

        baseline.Amount.Should().Be(250m);
    }

    [Fact]
    public void ComputeIncrementalDelta_SubtractsPreviouslyAppliedFromIntended()
    {
        var incremental = OperationEffectCalculator.ComputeIncrementalDelta(Money(150m), Money(100m));

        incremental.Amount.Should().Be(50m);
    }
}
