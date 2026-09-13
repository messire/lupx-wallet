using FluentAssertions;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using Xunit;

namespace LupexWallet.UnitTests.Operations;

/// <summary>BehaviorKindMapping — перевод кода поведения ReferenceData.OperationBehaviorKind в локальный enum Operations.Domain.</summary>
public sealed class BehaviorKindMappingTests
{
    [Theory]
    [InlineData("Income", OperationEffectKind.Income)]
    [InlineData("Expense", OperationEffectKind.Expense)]
    [InlineData("Transfer", OperationEffectKind.Transfer)]
    [InlineData("Adjustment", OperationEffectKind.Adjustment)]
    public void Parse_KnownCode_ReturnsExpectedEffectKind(string code, OperationEffectKind expected)
    {
        var result = BehaviorKindMapping.Parse(code);

        result.Should().Be(expected);
    }

    [Fact]
    public void Parse_UnknownCode_ThrowsInvalidOperationException()
    {
        var act = () => BehaviorKindMapping.Parse("Unknown");

        act.Should().Throw<InvalidOperationException>();
    }
}
