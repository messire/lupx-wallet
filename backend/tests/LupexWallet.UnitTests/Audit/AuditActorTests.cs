using FluentAssertions;
using LupexWallet.Audit.Domain;
using Xunit;

namespace LupexWallet.UnitTests.Audit;

/// <summary>AuditActor VO (ddd-model.md §4) — User без SystemProcessName, System требует его.</summary>
public sealed class AuditActorTests
{
    [Fact]
    public void User_HasUserKindAndNoSystemProcessName()
    {
        var actor = AuditActor.User();

        actor.Kind.Should().Be(AuditActorKind.User);
        actor.SystemProcessName.Should().BeNull();
    }

    [Fact]
    public void System_ValidProcessName_HasSystemKindAndProcessName()
    {
        var actor = AuditActor.System("BalanceSnapshotSchedulerHostedService");

        actor.Kind.Should().Be(AuditActorKind.System);
        actor.SystemProcessName.Should().Be("BalanceSnapshotSchedulerHostedService");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void System_MissingProcessName_ThrowsAuditSystemProcessNameRequiredException(string? processName)
    {
        var act = () => AuditActor.System(processName!);

        act.Should().Throw<AuditSystemProcessNameRequiredException>();
    }
}
