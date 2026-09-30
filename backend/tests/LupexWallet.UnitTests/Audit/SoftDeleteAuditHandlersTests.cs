using FluentAssertions;
using LupexWallet.Audit.Application;
using LupexWallet.Audit.Domain;
using LupexWallet.Audit.Infrastructure;
using LupexWallet.BalanceHistory.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Operations.Domain;
using LupexWallet.SharedKernel;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Audit;

/// <summary>
/// Audit subscribers for the record lifecycle (ADR-0002, §3, §5; ADR-0006; R13): Operation/Transfer
/// Deleted and Restored take their diff from the ChangeTracker (covered by the integration tests —
/// here only the entity type/id/action routing with no tracked diff), while BalanceSnapshotDeleted
/// is enriched by hand from the event fields: entity_id = WalletId, SnapshotDate and Balance as the
/// values before removal.
/// </summary>
public sealed class SoftDeleteAuditHandlersTests
{
    private static readonly CurrencyId Currency = new(Guid.NewGuid());

    private readonly IAuditEntryRepository _repository = Substitute.For<IAuditEntryRepository>();
    private readonly IAuditUnitOfWork _unitOfWork = Substitute.For<IAuditUnitOfWork>();
    private readonly List<AuditEntry> _recorded = [];

    private AuditRecorder CreateRecorder()
    {
        var actorAccessor = Substitute.For<IAuditActorAccessor>();
        actorAccessor.Current.Returns(AuditActorContext.User);
        _repository.When(r => r.Add(Arg.Any<AuditEntry>())).Do(call => _recorded.Add(call.Arg<AuditEntry>()));
        return new AuditRecorder(_repository, _unitOfWork, actorAccessor, new DispatchDomainEventsInterceptor(Substitute.For<IDomainEventDispatcher>()));
    }

    [Fact]
    public async Task OperationRestored_RecordsRestoredActionForOperationEntity()
    {
        var operationId = OperationId.New();
        var handler = new OperationRestoredAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<OperationRestored>(new OperationRestored(operationId, WalletId.New(), new DateOnly(2026, 1, 1))),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Operation");
        entry.EntityId.Should().Be(operationId.Value);
        entry.Action.Should().Be("Restored");
        entry.Actor.Kind.Should().Be(AuditActorKind.User);
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OperationDeleted_RecordsDeletedActionForOperationEntity()
    {
        var operationId = OperationId.New();
        var handler = new OperationDeletedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<OperationDeleted>(new OperationDeleted(operationId, WalletId.New(), new DateOnly(2026, 1, 1))),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Operation");
        entry.EntityId.Should().Be(operationId.Value);
        entry.Action.Should().Be("Deleted");
    }

    [Fact]
    public async Task TransferRestored_RecordsRestoredActionForTransferEntity()
    {
        var transferId = TransferId.New();
        var handler = new TransferRestoredAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<TransferRestored>(new TransferRestored(transferId)), CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Transfer");
        entry.EntityId.Should().Be(transferId.Value);
        entry.Action.Should().Be("Restored");
    }

    [Fact]
    public async Task TransferDeleted_RecordsDeletedActionForTransferEntity()
    {
        var transferId = TransferId.New();
        var handler = new TransferDeletedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<TransferDeleted>(new TransferDeleted(transferId)), CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("Transfer");
        entry.EntityId.Should().Be(transferId.Value);
        entry.Action.Should().Be("Deleted");
    }

    [Fact]
    public async Task BalanceSnapshotDeleted_RecordsSnapshotDeletedWithWalletIdAndValuesBeforeRemoval()
    {
        var walletId = WalletId.New();
        var handler = new BalanceSnapshotDeletedAuditHandler(CreateRecorder());

        await handler.Handle(
            new DomainEventNotification<BalanceSnapshotDeleted>(
                new BalanceSnapshotDeleted(walletId, new DateOnly(2026, 3, 9), new Money(1234.56m, Currency))),
            CancellationToken.None);

        var entry = _recorded.Should().ContainSingle().Subject;
        entry.EntityType.Should().Be("BalanceSnapshot");
        entry.EntityId.Should().Be(walletId.Value, "entity_id is the history owner: the snapshot has a composite key, no surrogate id");
        entry.Action.Should().Be("SnapshotDeleted");
        entry.Changes.Should().BeEquivalentTo(new[]
        {
            new AuditFieldChange("SnapshotDate", "2026-03-09", null),
            new AuditFieldChange("Balance", "1234.56", null),
        });
        await _unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BalanceSnapshotDeleted_BalanceIsFormattedWithInvariantCulture()
    {
        var handler = new BalanceSnapshotDeletedAuditHandler(CreateRecorder());
        var previousCulture = Thread.CurrentThread.CurrentCulture;
        Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo("ru-RU");
        try
        {
            await handler.Handle(
                new DomainEventNotification<BalanceSnapshotDeleted>(
                    new BalanceSnapshotDeleted(WalletId.New(), new DateOnly(2026, 3, 9), new Money(0.5m, Currency))),
                CancellationToken.None);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = previousCulture;
        }

        _recorded.Single().Changes.Single(c => c.FieldName == "Balance").OldValue.Should().Be("0.5");
    }
}
