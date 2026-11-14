using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>
/// OperationEffectCalculator — вычисление знаковой дельты для Income/Expense/Adjustment
/// (режим Delta; новое значение баланса — отдельная корректирующая сумма, ADR-0003 §6) и защита от прямого создания операции с
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
        var delta = OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Income, Money(100m), null);

        delta.Amount.Should().Be(100m);
    }

    [Fact]
    public void ComputeIntendedDelta_Expense_ReturnsNegatedAmount()
    {
        var delta = OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Expense, Money(40m), null);

        delta.Amount.Should().Be(-40m);
    }

    [Fact]
    public void ComputeIntendedDelta_AdjustmentDelta_ReturnsAmountAsIs()
    {
        var delta = OperationEffectCalculator.ComputeIntendedDelta(
            OperationEffectKind.Adjustment, Money(-15m), AdjustmentMode.Delta);

        delta.Amount.Should().Be(-15m);
    }

    [Fact]
    public void ComputeIntendedDelta_Transfer_ThrowsDirectOperationCreationNotAllowedForBehaviorException()
    {
        var act = () => OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Transfer, Money(10m), null);

        act.Should().Throw<DirectOperationCreationNotAllowedForBehaviorException>();
    }

    [Fact]
    public void ComputeIntendedDelta_AdjustmentWithoutMode_ThrowsAdjustmentModeRequiredException()
    {
        var act = () => OperationEffectCalculator.ComputeIntendedDelta(OperationEffectKind.Adjustment, Money(10m), null);

        act.Should().Throw<AdjustmentModeRequiredException>();
    }

    [Theory]
    [InlineData(OperationEffectKind.Income)]
    [InlineData(OperationEffectKind.Expense)]
    public void ComputeIntendedDelta_NonAdjustmentWithMode_ThrowsAdjustmentModeNotAllowedException(OperationEffectKind kind)
    {
        var act = () => OperationEffectCalculator.ComputeIntendedDelta(kind, Money(10m), AdjustmentMode.Delta);

        act.Should().Throw<AdjustmentModeNotAllowedException>();
    }

    [Fact]
    public void ComputeIncrementalDelta_SubtractsPreviouslyAppliedFromIntended()
    {
        var incremental = OperationEffectCalculator.ComputeIncrementalDelta(Money(150m), Money(100m));

        incremental.Amount.Should().Be(50m);
    }
}
