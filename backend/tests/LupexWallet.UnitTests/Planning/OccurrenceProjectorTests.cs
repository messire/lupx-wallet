using FluentAssertions;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using Xunit;
using static LupexWallet.UnitTests.Planning.PlanningTestData;

namespace LupexWallet.UnitTests.Planning;

/// <summary>
/// Вычисление наступлений (ADR-0010 §6): статусы, частичное исполнение, удалённая операция исполнения,
/// переносы через границы периода, просрочка, завершённый план.
/// </summary>
public sealed class OccurrenceProjectorTests
{
    private static readonly DateOnly Today = D(2026, 3, 15);

    private static IReadOnlyList<PlannedOccurrence> Project(PlannedTransaction plan, DateOnly from, DateOnly to,
        IReadOnlyDictionary<OperationId, ExecutionOperationFact>? facts = null) =>
        OccurrenceProjector.Project(plan, from, to, Today, facts ?? NoFacts);

    [Fact]
    public void Plain_Occurrences_AreExpected_PastOnesOverdue()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);

        var occurrences = Project(plan, D(2026, 1, 1), D(2026, 4, 30));

        occurrences.Select(o => o.EffectiveDate).Should().Equal(D(2026, 1, 10), D(2026, 2, 10), D(2026, 3, 10), D(2026, 4, 10));
        occurrences.Should().OnlyContain(o => o.Status == PlannedOccurrenceStatus.Expected && o.ExpectedAmount == 100m);
        occurrences.Select(o => o.IsOverdue).Should().Equal(true, true, true, false);
    }

    [Fact]
    public void PartialExecution_KeepsRemainderExpected_FullExecutionCompletes()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        Execute(plan, D(2026, 1, 10), 30m);

        var partial = Current(plan, D(2026, 1, 10), Today);
        partial.Status.Should().Be(PlannedOccurrenceStatus.PartiallyExecuted);
        partial.ExecutedAmount.Should().Be(30m);
        partial.ExpectedAmount.Should().Be(70m);
        partial.IsOverdue.Should().BeTrue();

        Execute(plan, D(2026, 1, 10), 70m);
        var done = Current(plan, D(2026, 1, 10), Today);
        done.Status.Should().Be(PlannedOccurrenceStatus.Executed);
        done.ExpectedAmount.Should().Be(0m);
        done.IsOverdue.Should().BeFalse();
        done.Executions.Should().HaveCount(2);
    }

    [Fact]
    public void SkippingAPartialOccurrence_ExpectsNothingMore()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        Execute(plan, D(2026, 1, 10), 30m);
        plan.SkipOccurrence(Current(plan, D(2026, 1, 10)));

        var occurrence = Current(plan, D(2026, 1, 10), Today);
        occurrence.Status.Should().Be(PlannedOccurrenceStatus.Skipped);
        occurrence.ExecutedAmount.Should().Be(30m);
        occurrence.ExpectedAmount.Should().Be(0m);
        occurrence.IsOverdue.Should().BeFalse();
    }

    [Fact]
    public void SoftDeletedExecutionOperation_ReturnsOccurrenceToExpected_EditedAmountIsUsed()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        var operationId = Execute(plan, D(2026, 1, 10), 100m);

        var deleted = new Dictionary<OperationId, ExecutionOperationFact> { [operationId] = new(100m, IsDeleted: true) };
        var afterDelete = Current(plan, D(2026, 1, 10), Today, deleted);
        afterDelete.Status.Should().Be(PlannedOccurrenceStatus.Expected);
        afterDelete.ExpectedAmount.Should().Be(100m);
        afterDelete.Executions.Should().ContainSingle().Which.IsOperationDeleted.Should().BeTrue();

        var edited = new Dictionary<OperationId, ExecutionOperationFact> { [operationId] = new(60m, IsDeleted: false) };
        var afterEdit = Current(plan, D(2026, 1, 10), Today, edited);
        afterEdit.Status.Should().Be(PlannedOccurrenceStatus.PartiallyExecuted);
        afterEdit.ExpectedAmount.Should().Be(40m);
    }

    [Fact]
    public void ExecutionAboveRemainder_IsExecuted_WithZeroExpected()
    {
        var plan = OneOff(D(2026, 3, 1), amount: 100m);
        Execute(plan, D(2026, 3, 1), 130m);

        var occurrence = Current(plan, D(2026, 3, 1), Today);
        occurrence.Status.Should().Be(PlannedOccurrenceStatus.Executed);
        occurrence.ExpectedAmount.Should().Be(0m);
    }

    [Fact]
    public void AmountOverride_BelowExecuted_MakesOccurrenceExecuted()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        Execute(plan, D(2026, 1, 10), 50m);
        plan.ChangeOccurrenceAmount(Current(plan, D(2026, 1, 10)), 50m);

        var occurrence = Current(plan, D(2026, 1, 10), Today);
        occurrence.Status.Should().Be(PlannedOccurrenceStatus.Executed);
        occurrence.IsAmountChanged.Should().BeTrue();
    }

    [Fact]
    public void Move_IntoRange_FromOutside_IsIncluded()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.MoveOccurrence(Current(plan, D(2026, 5, 10)), D(2026, 4, 2));

        var occurrences = Project(plan, D(2026, 4, 1), D(2026, 4, 5));

        var moved = occurrences.Should().ContainSingle().Subject;
        moved.OccurrenceDate.Should().Be(D(2026, 5, 10));
        moved.EffectiveDate.Should().Be(D(2026, 4, 2));
        moved.IsMoved.Should().BeTrue();
    }

    [Fact]
    public void Move_OutOfRange_IsExcluded_AndSortedByEffectiveDate()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.MoveOccurrence(Current(plan, D(2026, 2, 10)), D(2026, 4, 20));

        Project(plan, D(2026, 2, 1), D(2026, 2, 28)).Should().BeEmpty();
        Project(plan, D(2026, 1, 1), D(2026, 4, 30)).Select(o => o.OccurrenceDate)
            .Should().Equal(D(2026, 1, 10), D(2026, 3, 10), D(2026, 4, 10), D(2026, 2, 10));
    }

    [Fact]
    public void MovedOverride_OfDateNoLongerInSchedule_IsIgnored()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.MoveOccurrence(Current(plan, D(2026, 6, 10)), D(2026, 2, 1));
        plan.Update(plan.OperationTypeId, plan.Direction, plan.Amount, Currency, null, null, plan.StartDate,
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, 2));

        Project(plan, D(2026, 1, 1), D(2026, 12, 31)).Select(o => o.EffectiveDate).Should().Equal(D(2026, 1, 10), D(2026, 2, 10));
    }

    [Fact]
    public void MoveIntoFuture_ClearsOverdue()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.MoveOccurrence(Current(plan, D(2026, 3, 10)), D(2026, 3, 20));
        Current(plan, D(2026, 3, 10), Today).IsOverdue.Should().BeFalse();
    }

    [Fact]
    public void FinishedPlan_ListsOnlyExecutedOccurrences_ExpectingNothing()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        Execute(plan, D(2026, 1, 10), 40m);
        plan.Finish();

        var occurrences = Project(plan, D(2026, 1, 1), D(2026, 12, 31));

        var only = occurrences.Should().ContainSingle().Subject;
        only.Status.Should().Be(PlannedOccurrenceStatus.PartiallyExecuted);
        only.ExpectedAmount.Should().Be(0m);
        only.IsOverdue.Should().BeFalse();
    }
}
