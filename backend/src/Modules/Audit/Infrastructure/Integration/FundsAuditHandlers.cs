using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Funds.Domain;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to Funds domain events (ADR-0006, ADR-0010 §8). Fund-level changes use the
/// ChangeTracker diff of the fund row. Allocations are child entities, invisible to that diff,
/// so reserve/release entries are written on the fund with an explicit allocation change
/// (targeted enrichment, ADR-0006). Registered manually in AuditModuleExtensions.
/// </summary>
public sealed class FundCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<FundCreated>>
{
    public Task Handle(DomainEventNotification<FundCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Fund", notification.DomainEvent.FundId.Value, "Created", cancellationToken);
}

public sealed class FundUpdatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<FundUpdated>>
{
    public Task Handle(DomainEventNotification<FundUpdated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Fund", notification.DomainEvent.FundId.Value, "Updated", cancellationToken);
}

public sealed class FundClosedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<FundClosed>>
{
    public Task Handle(DomainEventNotification<FundClosed> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Fund", notification.DomainEvent.FundId.Value, "Closed", cancellationToken);
}

public sealed class FundDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<FundDeleted>>
{
    public Task Handle(DomainEventNotification<FundDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Fund", notification.DomainEvent.FundId.Value, "Deleted", cancellationToken);
}

public sealed class FundReservedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<FundReserved>>
{
    public Task Handle(DomainEventNotification<FundReserved> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return FundAllocationAudit.RecordAsync(
            recorder, interceptor, e, e.FundId.Value, "Reserved", e.WalletId.Value, e.OldAllocation, e.NewAllocation,
            e.Reason == FundReserveReason.Manual ? null : ("ReserveReason", e.Reason.ToString()), e.OperationId?.Value, cancellationToken);
    }
}

public sealed class FundReleasedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<FundReleased>>
{
    public Task Handle(DomainEventNotification<FundReleased> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return FundAllocationAudit.RecordAsync(
            recorder, interceptor, e, e.FundId.Value, "Released", e.WalletId.Value, e.OldAllocation, e.NewAllocation,
            ("ReleaseReason", e.Reason.ToString()), e.OperationId?.Value, cancellationToken);
    }
}

/// <summary>
/// One entry per allocation change: field "Allocation[walletId]" with old/new amount, plus the reason
/// (ReleaseReason always; ReserveReason when not manual) and the payment OperationId for B9 changes.
/// </summary>
internal static class FundAllocationAudit
{
    public static Task RecordAsync(
        AuditRecorder recorder,
        DispatchDomainEventsInterceptor interceptor,
        LupexWallet.SharedKernel.IDomainEvent domainEvent,
        Guid fundId,
        string action,
        Guid walletId,
        decimal oldAmount,
        decimal newAmount,
        (string Field, string Value)? reason,
        Guid? operationId,
        CancellationToken cancellationToken)
    {
        // The fund-row diff is shared by all events of this save; drop it so it is not kept in the scope.
        _ = interceptor.TakeChanges(domainEvent.EventId);

        var changes = new List<AuditFieldChange>
        {
            new($"Allocation[{walletId}]", Format(oldAmount), Format(newAmount)),
        };
        if (reason is { } r)
        {
            changes.Add(new AuditFieldChange(r.Field, null, r.Value));
        }

        if (operationId is { } id)
        {
            changes.Add(new AuditFieldChange("OperationId", null, id.ToString()));
        }

        return recorder.RecordAsync("Fund", fundId, action, changes, cancellationToken);
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
