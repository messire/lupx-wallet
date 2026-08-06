using FluentAssertions;
using LupexWallet.ReferenceData.Domain;
using LupexWallet.SharedKernel;
using Xunit;

namespace LupexWallet.UnitTests.ReferenceData;

/// <summary>OperationType (ddd-model.md, §2.2, §5): BehaviorKindId неизменяем после создания (проверяется отсутствием сеттера/метода Update).</summary>
public sealed class OperationTypeTests
{
    [Fact]
    public void Create_WithBlankName_ThrowsReferenceItemNameRequiredException()
    {
        var act = () => OperationType.Create(OperationTypeId.New(), " ", new OperationBehaviorKindId(Guid.NewGuid()));

        act.Should().Throw<ReferenceItemNameRequiredException>();
    }

    [Fact]
    public void Create_SetsBehaviorKindIdAndIsActive()
    {
        var behaviorKindId = new OperationBehaviorKindId(Guid.NewGuid());

        var operationType = OperationType.Create(OperationTypeId.New(), "Зарплата", behaviorKindId);

        operationType.BehaviorKindId.Should().Be(behaviorKindId);
        operationType.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var operationType = OperationType.Create(OperationTypeId.New(), "Зарплата", new OperationBehaviorKindId(Guid.NewGuid()));

        operationType.Deactivate();

        operationType.IsActive.Should().BeFalse();
    }

    [Fact]
    public void EnsureCanBeDeleted_WhenUsed_ThrowsReferenceItemInUseException()
    {
        var operationType = OperationType.Create(OperationTypeId.New(), "Зарплата", new OperationBehaviorKindId(Guid.NewGuid()));

        var act = () => operationType.EnsureCanBeDeleted(isUsed: true);

        act.Should().Throw<ReferenceItemInUseException>();
    }
}
