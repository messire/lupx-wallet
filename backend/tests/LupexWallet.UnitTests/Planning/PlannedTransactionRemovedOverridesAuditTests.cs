using FluentAssertions;
using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Planning.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;
using static LupexWallet.UnitTests.Planning.PlanningTestData;

namespace LupexWallet.UnitTests.Planning;

/// <summary>
/// Review v0.4.0, R2 (ADR-0010 §8, ADR-0006): overrides dropped by a plan change are child rows the root diff does
/// not see. The Updated event carries them and the Audit record of the plan lists each one as <c>Override[{date}]</c>
/// with the old value and null.
/// </summary>
public sealed class PlannedTransactionRemovedOverridesAuditTests
{
    [Fact]
    public void Update_DropsOverridesOutsideNewSchedule_AndReportsThemInEvent()
    {
        var plan = Monthly(D(2026, 1, 15), amount: 100m);
        plan.SkipOccurrence(Current(plan, D(2026, 3, 15)));
        plan.MoveOccurrence(Current(plan, D(2026, 4, 15)), D(2026, 4, 20));
        plan.ChangeOccurrenceAmount(Current(plan, D(2026, 4, 15)), 150m);
        plan.ClearDomainEvents();

        plan.Update(plan.OperationTypeId, plan.Direction, plan.Amount, plan.CurrencyId, plan.WalletId, plan.Description,
            D(2026, 1, 10), plan.Recurrence);

        plan.Overrides.Should().BeEmpty();
        var updated = plan.DomainEvents.Should().ContainSingle().Which.Should().BeOfType<PlannedTransactionUpdated>().Subject;
        updated.RemovedOverrides.Should().Equal(
            new OccurrenceOverrideSnapshot(D(2026, 3, 15), true, null, null),
            new OccurrenceOverrideSnapshot(D(2026, 4, 15), false, D(2026, 4, 20), 150m));
    }

    [Fact]
    public void Update_KeepsOverridesInsideSchedule_EventHasNoRemovedOverrides()
    {
        var plan = Monthly(D(2026, 1, 15), amount: 100m);
        plan.SkipOccurrence(Current(plan, D(2026, 3, 15)));
        plan.ClearDomainEvents();

        plan.Update(plan.OperationTypeId, plan.Direction, 120m, plan.CurrencyId, plan.WalletId, plan.Description,
            plan.StartDate, plan.Recurrence);

        plan.Overrides.Should().ContainSingle();
        plan.DomainEvents.OfType<PlannedTransactionUpdated>().Single().RemovedOverrides.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdatedAuditHandler_RecordsRemovedOverridesAsFieldsOfThePlan()
    {
        var recorded = new List<AuditEntry>();
        var repository = Substitute.For<IAuditEntryRepository>();
        repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => recorded.Add(call.Arg<AuditEntry>()));
        var actorAccessor = Substitute.For<IAuditActorAccessor>();
        actorAccessor.Current.Returns(AuditActorContext.User);
        var interceptor = new DispatchDomainEventsInterceptor(Substitute.For<IDomainEventDispatcher>());
        var recorder = new AuditRecorder(repository, Substitute.For<IAuditUnitOfWork>(), actorAccessor, interceptor);

        var planId = PlannedTransactionId.New();
        var domainEvent = new PlannedTransactionUpdated(planId)
        {
            RemovedOverrides =
            [
                new OccurrenceOverrideSnapshot(D(2026, 3, 15), true, null, null),
                new OccurrenceOverrideSnapshot(D(2026, 4, 15), false, D(2026, 4, 20), 150m),
            ],
        };

        await new PlannedTransactionUpdatedAuditHandler(recorder, interceptor)
            .Handle(new DomainEventNotification<PlannedTransactionUpdated>(domainEvent), CancellationToken.None);

        var entry = recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("PlannedTransaction");
        entry.EntityId.Should().Be(planId.Value);
        entry.Action.Should().Be("Updated");
        entry.Changes.Select(c => (c.FieldName, c.OldValue, c.NewValue)).Should().Equal(
            ("Override[2026-03-15]", "Skipped", (string?)null),
            ("Override[2026-04-15]", "MovedTo=2026-04-20, Amount=150", (string?)null));
    }
}
