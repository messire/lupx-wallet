using FluentAssertions;
using LupexWallet.Audit.Domain;
using Xunit;

namespace LupexWallet.UnitTests.Audit;

/// <summary>AuditEntry (ddd-model.md §2.8) — агрегат append-only, только Create, без методов изменения.</summary>
public sealed class AuditEntryTests
{
    [Fact]
    public void Create_EmptyEntityType_ThrowsAuditEntityTypeRequiredException()
    {
        var act = () => AuditEntry.Create(string.Empty, Guid.NewGuid(), "Created", AuditActor.User());

        act.Should().Throw<AuditEntityTypeRequiredException>();
    }

    [Fact]
    public void Create_EmptyAction_ThrowsAuditActionRequiredException()
    {
        var act = () => AuditEntry.Create("Wallet", Guid.NewGuid(), string.Empty, AuditActor.User());

        act.Should().Throw<AuditActionRequiredException>();
    }

    [Fact]
    public void Create_ValidArguments_SetsAllFieldsAndDefaultsToEmptyChanges()
    {
        var entityId = Guid.NewGuid();
        var actor = AuditActor.User();

        var entry = AuditEntry.Create("Wallet", entityId, "Created", actor);

        entry.EntityType.Should().Be("Wallet");
        entry.EntityId.Should().Be(entityId);
        entry.Action.Should().Be("Created");
        entry.Actor.Should().Be(actor);
        entry.Changes.Should().BeEmpty();
        entry.Id.Should().NotBeNull();
    }

    [Fact]
    public void Create_WithChanges_PreservesChangesList()
    {
        var changes = new List<AuditFieldChange> { new("Name", "Old", "New") };

        var entry = AuditEntry.Create("Wallet", Guid.NewGuid(), "Updated", AuditActor.User(), changes);

        entry.Changes.Should().BeEquivalentTo(changes);
    }

    /// <summary>
    /// ADR-0010, раздел 3: OccurredAt берется из MonotonicClock (не DateTimeOffset.UtcNow
    /// напрямую) — строго возрастает даже при коллизии тиков системных часов, что курсорная
    /// пагинация GET /audit-entries использует как единственное поле курсора без дублей.
    /// </summary>
    [Fact]
    public void Create_CalledRepeatedly_OccurredAtIsStrictlyIncreasing()
    {
        var entries = Enumerable.Range(0, 50)
            .Select(_ => AuditEntry.Create("Wallet", Guid.NewGuid(), "Updated", AuditActor.User()))
            .ToList();

        entries.Select(e => e.OccurredAt).Should().BeInAscendingOrder();
        entries.Select(e => e.OccurredAt).Distinct().Should().HaveCount(entries.Count, "коллизии тиков не должны приводить к одинаковым меткам");
    }
}
