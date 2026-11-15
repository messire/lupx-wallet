using FluentAssertions;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using Xunit;
using static LupexWallet.UnitTests.Planning.PlanningTestData;

namespace LupexWallet.UnitTests.Planning;

/// <summary>Агрегат PlannedTransaction (ADR-0010 §6, §8): инварианты, отклонения, исполнения, жизненный цикл.</summary>
public sealed class PlannedTransactionTests
{
    private static readonly RecurrenceRule MonthlyRule = RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, null);

    [Fact]
    public void Create_RaisesCreated_AndStoresFlatRule()
    {
        var plan = Monthly(D(2026, 1, 31));

        plan.Status.Should().Be(PlannedTransactionStatus.Active);
        plan.Recurrence.Should().Be(MonthlyRule);
        plan.RecurrenceFrequency.Should().Be(RecurrenceFrequency.Monthly);
        plan.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PlannedTransactionCreated>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Create_NonPositiveAmount_Throws(decimal amount)
    {
        FluentActions.Invoking(() => Monthly(D(2026, 1, 1), amount)).Should().Throw<PlannedAmountMustBePositiveException>();
    }

    [Fact]
    public void Create_UntilBeforeStart_Throws()
    {
        FluentActions.Invoking(() => PlannedTransaction.Create(
                PlannedTransactionId.New(), new OperationTypeId(Guid.NewGuid()), PlannedDirection.Expense, 1m, Currency, null, null,
                D(2026, 5, 1), RecurrenceRule.Create(RecurrenceFrequency.Daily, 1, D(2026, 4, 30), null)))
            .Should().Throw<RecurrenceUntilBeforeStartException>();
    }

    [Fact]
    public void Create_TooLongDescription_Throws_BlankBecomesNull()
    {
        FluentActions.Invoking(() => PlannedTransaction.Create(
                PlannedTransactionId.New(), new OperationTypeId(Guid.NewGuid()), PlannedDirection.Expense, 1m, Currency, null,
                new string('x', PlannedTransaction.DescriptionMaxLength + 1), D(2026, 1, 1), null))
            .Should().Throw<PlannedDescriptionTooLongException>();

        PlannedTransaction.Create(
                PlannedTransactionId.New(), new OperationTypeId(Guid.NewGuid()), PlannedDirection.Expense, 1m, Currency, null,
                "   ", D(2026, 1, 1), null)
            .Description.Should().BeNull();
    }

    [Fact]
    public void Skip_Unskip_RemovesEmptyOverride()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.ClearDomainEvents();

        plan.SkipOccurrence(Current(plan, D(2026, 2, 10)));
        plan.FindOverride(D(2026, 2, 10))!.IsSkipped.Should().BeTrue();
        Current(plan, D(2026, 2, 10)).Status.Should().Be(PlannedOccurrenceStatus.Skipped);

        plan.UnskipOccurrence(Current(plan, D(2026, 2, 10)));
        plan.Overrides.Should().BeEmpty();
        plan.DomainEvents.Select(e => e.GetType()).Should().Equal(typeof(PlannedOccurrenceSkipped), typeof(PlannedOccurrenceUnskipped));
    }

    [Fact]
    public void Unskip_NotSkipped_Throws()
    {
        var plan = Monthly(D(2026, 1, 10));
        FluentActions.Invoking(() => plan.UnskipOccurrence(Current(plan, D(2026, 1, 10))))
            .Should().Throw<PlannedOccurrenceNotSkippedException>();
    }

    [Fact]
    public void Occurrence_NotInSchedule_IsNotFound()
    {
        var plan = Monthly(D(2026, 1, 10));
        FluentActions.Invoking(() => Current(plan, D(2026, 1, 11))).Should().Throw<PlannedOccurrenceNotFoundException>();
        FluentActions.Invoking(() => Current(plan, D(2025, 12, 10))).Should().Throw<PlannedOccurrenceNotFoundException>();
    }

    [Fact]
    public void Move_ToOriginalDate_UndoesMove_AndKeepsAmountOverride()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.MoveOccurrence(Current(plan, D(2026, 2, 10)), D(2026, 2, 14));
        plan.ChangeOccurrenceAmount(Current(plan, D(2026, 2, 10)), 150m);

        Current(plan, D(2026, 2, 10)).EffectiveDate.Should().Be(D(2026, 2, 14));

        plan.MoveOccurrence(Current(plan, D(2026, 2, 10)), D(2026, 2, 10));
        var occurrence = Current(plan, D(2026, 2, 10));
        occurrence.EffectiveDate.Should().Be(D(2026, 2, 10));
        occurrence.IsMoved.Should().BeFalse();
        occurrence.PlannedAmount.Should().Be(150m);
    }

    [Fact]
    public void ChangeAmount_ToPlanAmountOrNull_ClearsOverride_NonPositiveThrows()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        plan.ChangeOccurrenceAmount(Current(plan, D(2026, 1, 10)), 120m);
        plan.Overrides.Should().ContainSingle();

        plan.ChangeOccurrenceAmount(Current(plan, D(2026, 1, 10)), 100m);
        plan.Overrides.Should().BeEmpty();

        FluentActions.Invoking(() => plan.ChangeOccurrenceAmount(Current(plan, D(2026, 1, 10)), 0m))
            .Should().Throw<PlannedAmountMustBePositiveException>();
    }

    [Fact]
    public void Execution_SkippedOrFullyExecuted_Throws()
    {
        var plan = Monthly(D(2026, 1, 10), amount: 100m);
        Execute(plan, D(2026, 1, 10), 100m);
        FluentActions.Invoking(() => Execute(plan, D(2026, 1, 10), 1m)).Should().Throw<PlannedOccurrenceAlreadyExecutedException>();
        FluentActions.Invoking(() => plan.SkipOccurrence(Current(plan, D(2026, 1, 10))))
            .Should().Throw<PlannedOccurrenceAlreadyExecutedException>();

        plan.SkipOccurrence(Current(plan, D(2026, 2, 10)));
        FluentActions.Invoking(() => Execute(plan, D(2026, 2, 10), 1m)).Should().Throw<PlannedOccurrenceSkippedException>();
    }

    [Fact]
    public void Update_WithExecutions_CurrencyDirectionAndScheduleAreGuarded()
    {
        var plan = Monthly(D(2026, 1, 10));
        Execute(plan, D(2026, 2, 10), 40m);

        FluentActions.Invoking(() => plan.Update(plan.OperationTypeId, plan.Direction, 100m, new CurrencyId(Guid.NewGuid()), null, null,
            plan.StartDate, MonthlyRule)).Should().Throw<PlannedCurrencyChangeNotAllowedException>();

        FluentActions.Invoking(() => plan.Update(plan.OperationTypeId, PlannedDirection.Income, 100m, Currency, null, null,
            plan.StartDate, MonthlyRule)).Should().Throw<PlannedDirectionChangeNotAllowedException>();

        // 2026-02-10 would no longer be an occurrence.
        FluentActions.Invoking(() => plan.Update(plan.OperationTypeId, plan.Direction, 100m, Currency, null, null,
            D(2026, 1, 11), MonthlyRule)).Should().Throw<PlannedScheduleChangeConflictException>();

        // Ending the rule after the executed occurrence is fine.
        plan.Update(plan.OperationTypeId, plan.Direction, 120m, Currency, null, "Новая аренда", plan.StartDate,
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, D(2026, 3, 10), null));
        plan.Amount.Should().Be(120m);
        plan.RecurrenceUntil.Should().Be(D(2026, 3, 10));
    }

    [Fact]
    public void Update_DropsOverridesThatLeaveTheSchedule()
    {
        var plan = Monthly(D(2026, 1, 10));
        plan.SkipOccurrence(Current(plan, D(2026, 2, 10)));
        plan.SkipOccurrence(Current(plan, D(2026, 6, 10)));

        plan.Update(plan.OperationTypeId, plan.Direction, plan.Amount, Currency, null, null, plan.StartDate,
            RecurrenceRule.Create(RecurrenceFrequency.Monthly, 1, null, 3));

        plan.Overrides.Select(o => o.OccurrenceDate).Should().Equal(D(2026, 2, 10));
    }

    [Fact]
    public void Finish_BlocksChanges_AndFinishedTwiceThrows()
    {
        var plan = Monthly(D(2026, 1, 10));
        var current = Current(plan, D(2026, 1, 10));
        plan.Finish();

        plan.Status.Should().Be(PlannedTransactionStatus.Finished);
        plan.FinishedAt.Should().NotBeNull();
        FluentActions.Invoking(plan.Finish).Should().Throw<PlannedTransactionFinishedException>();
        FluentActions.Invoking(() => plan.SkipOccurrence(current)).Should().Throw<PlannedTransactionFinishedException>();
        FluentActions.Invoking(() => plan.Update(plan.OperationTypeId, plan.Direction, 1m, Currency, null, null, plan.StartDate, null))
            .Should().Throw<PlannedTransactionFinishedException>();
    }

    [Fact]
    public void MarkAsDeleted_WithExecutions_Throws()
    {
        var plan = OneOff(D(2026, 1, 10));
        Execute(plan, D(2026, 1, 10), 10m);
        FluentActions.Invoking(plan.MarkAsDeleted).Should().Throw<PlannedTransactionHasExecutionsException>();

        var empty = OneOff(D(2026, 1, 10));
        empty.ClearDomainEvents();
        empty.MarkAsDeleted();
        empty.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PlannedTransactionDeleted>();
    }

    [Fact]
    public void UnlinkWallet_ClearsWallet_OnceOnly()
    {
        var plan = Monthly(D(2026, 1, 10), walletId: WalletId.New());
        plan.ClearDomainEvents();

        plan.UnlinkWallet();
        plan.UnlinkWallet();

        plan.WalletId.Should().BeNull();
        plan.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PlannedTransactionUpdated>();
    }
}
