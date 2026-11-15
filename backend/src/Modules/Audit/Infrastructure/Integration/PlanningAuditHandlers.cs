using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.Planning.Domain;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to Planning events (ADR-0006, ADR-0010 §8). Entity type "PlannedTransaction", entity id —
/// the plan. Plan-level events use the ChangeTracker diff; occurrence events are child-row changes the
/// root diff does not see, so their fields are built from the event (OccurrenceDate identifies the occurrence).
/// </summary>
public static class PlanningAuditHandlers
{
    public const string EntityType = "PlannedTransaction";

    public static void Register(IServiceCollection services)
    {
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedTransactionCreated>>, PlannedTransactionCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedTransactionUpdated>>, PlannedTransactionUpdatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedTransactionFinished>>, PlannedTransactionFinishedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedTransactionDeleted>>, PlannedTransactionDeletedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedOccurrenceSkipped>>, PlannedOccurrenceSkippedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedOccurrenceUnskipped>>, PlannedOccurrenceUnskippedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedOccurrenceMoved>>, PlannedOccurrenceMovedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedOccurrenceAmountChanged>>, PlannedOccurrenceAmountChangedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<PlannedOccurrenceExecuted>>, PlannedOccurrenceExecutedAuditHandler>();
    }

    internal static AuditFieldChange OccurrenceDate(DateOnly date) =>
        new("OccurrenceDate", null, date.ToString("O", CultureInfo.InvariantCulture));

    internal static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    internal static string Format(DateOnly value) => value.ToString("O", CultureInfo.InvariantCulture);

    internal static string OverrideField(DateOnly occurrenceDate) => $"Override[{Format(occurrenceDate)}]";

    /// <summary>What a removed override did, e.g. "Skipped", "MovedTo=2026-04-20", "Amount=50" (comma-separated).</summary>
    internal static string Describe(OccurrenceOverrideSnapshot snapshot)
    {
        var parts = new List<string>();
        if (snapshot.IsSkipped)
        {
            parts.Add("Skipped");
        }

        if (snapshot.MovedToDate is { } movedTo)
        {
            parts.Add($"MovedTo={Format(movedTo)}");
        }

        if (snapshot.Amount is { } amount)
        {
            parts.Add($"Amount={Format(amount)}");
        }

        return string.Join(", ", parts);
    }
}

public sealed class PlannedTransactionCreatedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedTransactionCreated>>
{
    public Task Handle(DomainEventNotification<PlannedTransactionCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, PlanningAuditHandlers.EntityType, notification.DomainEvent.PlannedTransactionId.Value, "Created", cancellationToken);
}

/// <summary>
/// Root diff from the ChangeTracker plus one field per override the change dropped:
/// <c>Override[{occurrenceDate}]</c>, old value — what the override did, new value — null.
/// </summary>
public sealed class PlannedTransactionUpdatedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<PlannedTransactionUpdated>>
{
    public Task Handle(DomainEventNotification<PlannedTransactionUpdated> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        var changes = interceptor.TakeChanges(e.EventId)
            .Select(c => new AuditFieldChange(c.FieldName, c.OldValue, c.NewValue))
            .Concat(e.RemovedOverrides.Select(o => new AuditFieldChange(
                PlanningAuditHandlers.OverrideField(o.OccurrenceDate), PlanningAuditHandlers.Describe(o), null)))
            .ToList();
        return recorder.RecordAsync(PlanningAuditHandlers.EntityType, e.PlannedTransactionId.Value, "Updated", changes, cancellationToken);
    }
}

public sealed class PlannedTransactionFinishedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedTransactionFinished>>
{
    public Task Handle(DomainEventNotification<PlannedTransactionFinished> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, PlanningAuditHandlers.EntityType, notification.DomainEvent.PlannedTransactionId.Value, "Finished", cancellationToken);
}

public sealed class PlannedTransactionDeletedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedTransactionDeleted>>
{
    public Task Handle(DomainEventNotification<PlannedTransactionDeleted> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(
            notification.DomainEvent, PlanningAuditHandlers.EntityType, notification.DomainEvent.PlannedTransactionId.Value, "Deleted", cancellationToken);
}

public sealed class PlannedOccurrenceSkippedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedOccurrenceSkipped>>
{
    public Task Handle(DomainEventNotification<PlannedOccurrenceSkipped> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return recorder.RecordAsync(
            PlanningAuditHandlers.EntityType, e.PlannedTransactionId.Value, "OccurrenceSkipped",
            [PlanningAuditHandlers.OccurrenceDate(e.OccurrenceDate), new AuditFieldChange("IsSkipped", "False", "True")],
            cancellationToken);
    }
}

public sealed class PlannedOccurrenceUnskippedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedOccurrenceUnskipped>>
{
    public Task Handle(DomainEventNotification<PlannedOccurrenceUnskipped> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return recorder.RecordAsync(
            PlanningAuditHandlers.EntityType, e.PlannedTransactionId.Value, "OccurrenceUnskipped",
            [PlanningAuditHandlers.OccurrenceDate(e.OccurrenceDate), new AuditFieldChange("IsSkipped", "True", "False")],
            cancellationToken);
    }
}

public sealed class PlannedOccurrenceMovedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedOccurrenceMoved>>
{
    public Task Handle(DomainEventNotification<PlannedOccurrenceMoved> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return recorder.RecordAsync(
            PlanningAuditHandlers.EntityType, e.PlannedTransactionId.Value, "OccurrenceMoved",
            [
                PlanningAuditHandlers.OccurrenceDate(e.OccurrenceDate),
                new AuditFieldChange("EffectiveDate", PlanningAuditHandlers.Format(e.OldEffectiveDate), PlanningAuditHandlers.Format(e.NewEffectiveDate)),
            ],
            cancellationToken);
    }
}

public sealed class PlannedOccurrenceAmountChangedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedOccurrenceAmountChanged>>
{
    public Task Handle(DomainEventNotification<PlannedOccurrenceAmountChanged> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return recorder.RecordAsync(
            PlanningAuditHandlers.EntityType, e.PlannedTransactionId.Value, "OccurrenceAmountChanged",
            [
                PlanningAuditHandlers.OccurrenceDate(e.OccurrenceDate),
                new AuditFieldChange("Amount", PlanningAuditHandlers.Format(e.OldAmount), PlanningAuditHandlers.Format(e.NewAmount)),
            ],
            cancellationToken);
    }
}

public sealed class PlannedOccurrenceExecutedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<PlannedOccurrenceExecuted>>
{
    public Task Handle(DomainEventNotification<PlannedOccurrenceExecuted> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return recorder.RecordAsync(
            PlanningAuditHandlers.EntityType, e.PlannedTransactionId.Value, "OccurrenceExecuted",
            [
                PlanningAuditHandlers.OccurrenceDate(e.OccurrenceDate),
                new AuditFieldChange("OperationId", null, e.OperationId.Value.ToString()),
                new AuditFieldChange("Amount", null, PlanningAuditHandlers.Format(e.Amount)),
            ],
            cancellationToken);
    }
}
