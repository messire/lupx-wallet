using FluentAssertions;
using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Audit;

/// <summary>
/// AuditRecorder (ADR-0010) — единая точка записи AuditEntry, используемая всеми
/// *AuditHandlers.cs. Полный расчет diff из EF Core ChangeTracker
/// (DispatchDomainEventsInterceptor.ComputeFieldChanges) требует реального DbContext и
/// покрыт интеграционными тестами (AuditApiTests — changes видны в ответе GET
/// /audit-entries для реальных CRUD-команд); здесь — только контракт AuditRecorder
/// самого по себе, включая разметку actorKind (User/System, W2.2 DoD п.4).
/// </summary>
public sealed class AuditRecorderTests
{
    private sealed record FakeDomainEvent : DomainEvent;

    private static (AuditRecorder Recorder, IAuditEntryRepository Repository, IAuditUnitOfWork UnitOfWork, IAuditActorAccessor ActorAccessor) CreateSut()
    {
        var repository = Substitute.For<IAuditEntryRepository>();
        var unitOfWork = Substitute.For<IAuditUnitOfWork>();
        var actorAccessor = Substitute.For<IAuditActorAccessor>();
        actorAccessor.Current.Returns(AuditActorContext.User);
        var interceptor = new DispatchDomainEventsInterceptor(Substitute.For<IDomainEventDispatcher>());

        return (new AuditRecorder(repository, unitOfWork, actorAccessor, interceptor), repository, unitOfWork, actorAccessor);
    }

    [Fact]
    public async Task RecordAsync_DefaultActorAccessor_CreatesEntryWithUserActorAndPersistsIt()
    {
        var (recorder, repository, unitOfWork, _) = CreateSut();
        AuditEntry? recorded = null;
        repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => recorded = call.Arg<AuditEntry>());
        var entityId = Guid.NewGuid();
        var changes = new List<AuditFieldChange> { new("Name", "Old", "New") };

        await recorder.RecordAsync("Wallet", entityId, "Updated", changes, CancellationToken.None);

        repository.Received(1).Add(Arg.Any<AuditEntry>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        recorded.Should().NotBeNull();
        recorded!.EntityType.Should().Be("Wallet");
        recorded.EntityId.Should().Be(entityId);
        recorded.Action.Should().Be("Updated");
        recorded.Actor.Kind.Should().Be(AuditActorKind.User);
        recorded.Actor.SystemProcessName.Should().BeNull();
        recorded.Changes.Should().BeEquivalentTo(changes);
    }

    /// <summary>W2.2 DoD п.4: actorKind = System для записей фоновых задач (ADR-0010, "Механизм актора").</summary>
    [Fact]
    public async Task RecordAsync_SystemActorAccessor_CreatesEntryWithSystemActorAndProcessName()
    {
        var (recorder, repository, _, actorAccessor) = CreateSut();
        actorAccessor.Current.Returns(new AuditActorContext(IsSystem: true, SystemProcessName: "BalanceSnapshotSchedulerHostedService"));
        AuditEntry? recorded = null;
        repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => recorded = call.Arg<AuditEntry>());

        await recorder.RecordAsync("BalanceSnapshot", Guid.NewGuid(), "SnapshotCreated", [], CancellationToken.None);

        recorded!.Actor.Kind.Should().Be(AuditActorKind.System);
        recorded.Actor.SystemProcessName.Should().Be("BalanceSnapshotSchedulerHostedService");
    }

    [Fact]
    public async Task RecordAsync_SystemActorWithoutProcessName_FallsBackToUnknown()
    {
        var (recorder, repository, _, actorAccessor) = CreateSut();
        actorAccessor.Current.Returns(new AuditActorContext(IsSystem: true, SystemProcessName: null));
        AuditEntry? recorded = null;
        repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => recorded = call.Arg<AuditEntry>());

        await recorder.RecordAsync("BalanceSnapshot", Guid.NewGuid(), "SnapshotUpdated", [], CancellationToken.None);

        recorded!.Actor.SystemProcessName.Should().Be("Unknown");
    }

    /// <summary>
    /// Diff по умолчанию берется из DispatchDomainEventsInterceptor.TakeChanges(EventId) — если
    /// для события ничего не было вычислено (ChangeTracker-механизм не сработал ни разу на
    /// этом интерцепторе, как в этом изолированном юнит-тесте без реального DbContext),
    /// TakeChanges возвращает пустой список (см. класс интерцептора) — RecordFromChangeTrackerAsync
    /// не должен падать, просто создает запись с пустым changes.
    /// </summary>
    [Fact]
    public async Task RecordFromChangeTrackerAsync_NoChangesComputedForEvent_CreatesEntryWithEmptyChanges()
    {
        var (recorder, repository, unitOfWork, _) = CreateSut();
        AuditEntry? recorded = null;
        repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => recorded = call.Arg<AuditEntry>());
        var domainEvent = new FakeDomainEvent();
        var entityId = Guid.NewGuid();

        await recorder.RecordFromChangeTrackerAsync(domainEvent, "Wallet", entityId, "Created", CancellationToken.None);

        repository.Received(1).Add(Arg.Any<AuditEntry>());
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        recorded!.EntityType.Should().Be("Wallet");
        recorded.EntityId.Should().Be(entityId);
        recorded.Action.Should().Be("Created");
        recorded.Changes.Should().BeEmpty();
    }
}
