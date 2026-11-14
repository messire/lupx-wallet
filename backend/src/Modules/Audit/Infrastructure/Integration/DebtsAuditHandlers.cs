using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Debts.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to Debts events (ADR-0006, ADR-0010 §8). Repayments, payments and schedule items
/// are child entities: the root's ChangeTracker diff (UpdatedAt) is enriched with the child's
/// values taken from the event, so the change lands in the owner's record.
/// </summary>
public sealed class CreditorCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CreditorCreated>>
{
    public Task Handle(DomainEventNotification<CreditorCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Creditor", notification.DomainEvent.CreditorId.Value, "Created", cancellationToken);
}

public sealed class CreditorRenamedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CreditorRenamed>>
{
    public Task Handle(DomainEventNotification<CreditorRenamed> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Creditor", notification.DomainEvent.CreditorId.Value, "Updated", cancellationToken);
}

public sealed class CreditorDeactivatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CreditorDeactivated>>
{
    public Task Handle(DomainEventNotification<CreditorDeactivated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Creditor", notification.DomainEvent.CreditorId.Value, "Deactivated", cancellationToken);
}

public sealed class CreditorDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<CreditorDeleted>>
{
    public Task Handle(DomainEventNotification<CreditorDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Creditor", notification.DomainEvent.CreditorId.Value, "Deleted", cancellationToken);
}

public sealed class DebtCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<DebtCreated>>
{
    public Task Handle(DomainEventNotification<DebtCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Debt", notification.DomainEvent.DebtId.Value, "Created", cancellationToken);
}

public sealed class DebtUpdatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<DebtUpdated>>
{
    public Task Handle(DomainEventNotification<DebtUpdated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Debt", notification.DomainEvent.DebtId.Value, "Updated", cancellationToken);
}

public sealed class DebtCancelledAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<DebtCancelled>>
{
    public Task Handle(DomainEventNotification<DebtCancelled> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Debt", notification.DomainEvent.DebtId.Value, "Cancelled", cancellationToken);
}

public sealed class DebtDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<DebtDeleted>>
{
    public Task Handle(DomainEventNotification<DebtDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Debt", notification.DomainEvent.DebtId.Value, "Deleted", cancellationToken);
}

public sealed class DebtRepaymentRecordedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<DebtRepaymentRecorded>>
{
    public Task Handle(DomainEventNotification<DebtRepaymentRecorded> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return DebtsAuditEnrichment.RecordAsync(
            recorder, interceptor, e, "Debt", e.DebtId.Value, "RepaymentRecorded",
            DebtsAuditEnrichment.PaymentChanges("Repayment", e.OperationId, e.WalletId, e.Amount, e.RepaymentDate), cancellationToken);
    }
}

public sealed class LoanCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<LoanCreated>>
{
    public Task Handle(DomainEventNotification<LoanCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Loan", notification.DomainEvent.LoanId.Value, "Created", cancellationToken);
}

public sealed class LoanUpdatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<LoanUpdated>>
{
    public Task Handle(DomainEventNotification<LoanUpdated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Loan", notification.DomainEvent.LoanId.Value, "Updated", cancellationToken);
}

public sealed class LoanCancelledAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<LoanCancelled>>
{
    public Task Handle(DomainEventNotification<LoanCancelled> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Loan", notification.DomainEvent.LoanId.Value, "Cancelled", cancellationToken);
}

public sealed class LoanDeletedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<LoanDeleted>>
{
    public Task Handle(DomainEventNotification<LoanDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, "Loan", notification.DomainEvent.LoanId.Value, "Deleted", cancellationToken);
}

public sealed class LoanScheduleChangedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<LoanScheduleChanged>>
{
    public Task Handle(DomainEventNotification<LoanScheduleChanged> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var change = new AuditFieldChange(
            "Schedule", DebtsAuditEnrichment.FormatSchedule(e.PreviousSchedule), DebtsAuditEnrichment.FormatSchedule(e.NewSchedule));
        return DebtsAuditEnrichment.RecordAsync(recorder, interceptor, e, "Loan", e.LoanId.Value, "ScheduleChanged", [change], cancellationToken);
    }
}

public sealed class LoanPaymentRecordedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<LoanPaymentRecorded>>
{
    public Task Handle(DomainEventNotification<LoanPaymentRecorded> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return DebtsAuditEnrichment.RecordAsync(
            recorder, interceptor, e, "Loan", e.LoanId.Value, "PaymentRecorded",
            DebtsAuditEnrichment.PaymentChanges("Payment", e.OperationId, e.WalletId, e.Amount, e.PaymentDate), cancellationToken);
    }
}

internal static class DebtsAuditEnrichment
{
    public static Task RecordAsync(
        AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor, IDomainEvent domainEvent,
        string entityType, Guid entityId, string action, IReadOnlyList<AuditFieldChange> extra, CancellationToken cancellationToken)
    {
        var changes = interceptor.TakeChanges(domainEvent.EventId)
            .Select(c => new AuditFieldChange(c.FieldName, c.OldValue, c.NewValue))
            .Concat(extra)
            .ToList();
        return recorder.RecordAsync(entityType, entityId, action, changes, cancellationToken);
    }

    public static IReadOnlyList<AuditFieldChange> PaymentChanges(
        string prefix, OperationId operationId, WalletId walletId, Money amount, DateOnly date) =>
    [
        new($"{prefix}.OperationId", null, operationId.Value.ToString()),
        new($"{prefix}.WalletId", null, walletId.Value.ToString()),
        new($"{prefix}.Amount", null, amount.Amount.ToString(CultureInfo.InvariantCulture)),
        new($"{prefix}.Date", null, date.ToString("O", CultureInfo.InvariantCulture)),
    ];

    /// <summary>"yyyy-MM-dd:amount; ..." or null for an empty schedule.</summary>
    public static string? FormatSchedule(IReadOnlyList<LoanScheduleEntry> schedule) =>
        schedule.Count == 0
            ? null
            : string.Join("; ", schedule.Select(x =>
                $"{x.DueDate.ToString("O", CultureInfo.InvariantCulture)}:{x.Amount.ToString(CultureInfo.InvariantCulture)}"));
}
