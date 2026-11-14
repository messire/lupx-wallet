using System.Globalization;
using LupexWallet.Audit.Domain;
using LupexWallet.Budgeting.Domain;
using LupexWallet.BuildingBlocks.Infrastructure;
using LupexWallet.SharedKernel;
using MediatR;
using Microsoft.Extensions.DependencyInjection;

namespace LupexWallet.Audit.Infrastructure;

/// <summary>
/// Subscribers to Budgeting events (ADR-0006, ADR-0010 §8). The root diff comes from the ChangeTracker;
/// child rows (member types of a category, limits of a budget) are added from the event, so their
/// changes land in the owner's record: "OperationTypeIds" and "Limit[{categoryId}]".
/// </summary>
internal static class BudgetingAudit
{
    public const string ExpenseCategory = "ExpenseCategory";
    public const string Budget = "Budget";

    public static Task RecordAsync(
        AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor, IDomainEvent domainEvent,
        string entityType, Guid entityId, string action, IEnumerable<AuditFieldChange> extra, CancellationToken cancellationToken)
    {
        var changes = interceptor.TakeChanges(domainEvent.EventId)
            .Select(c => new AuditFieldChange(c.FieldName, c.OldValue, c.NewValue))
            .Concat(extra)
            .ToList();
        return recorder.RecordAsync(entityType, entityId, action, changes, cancellationToken);
    }

    public static string? Join(IReadOnlyList<OperationTypeId>? ids) =>
        ids is null ? null : string.Join(",", ids.Select(id => id.Value.ToString()));

    public static string? Format(decimal? value) => value?.ToString(CultureInfo.InvariantCulture);

    public static string LimitField(ExpenseCategoryId id) => $"Limit[{id.Value}]";
}

public sealed class ExpenseCategoryCreatedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<ExpenseCategoryCreated>>
{
    public Task Handle(DomainEventNotification<ExpenseCategoryCreated> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return BudgetingAudit.RecordAsync(recorder, interceptor, e, BudgetingAudit.ExpenseCategory, e.ExpenseCategoryId.Value, "Created",
            [new AuditFieldChange("OperationTypeIds", null, BudgetingAudit.Join(e.OperationTypeIds))], cancellationToken);
    }
}

public sealed class ExpenseCategoryUpdatedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<ExpenseCategoryUpdated>>
{
    public Task Handle(DomainEventNotification<ExpenseCategoryUpdated> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        IEnumerable<AuditFieldChange> extra = e.OldOperationTypeIds is null
            ? []
            : [new AuditFieldChange("OperationTypeIds", BudgetingAudit.Join(e.OldOperationTypeIds), BudgetingAudit.Join(e.NewOperationTypeIds))];
        return BudgetingAudit.RecordAsync(recorder, interceptor, e, BudgetingAudit.ExpenseCategory, e.ExpenseCategoryId.Value, "Updated", extra, cancellationToken);
    }
}

public sealed class ExpenseCategoryDeactivatedAuditHandler(AuditRecorder recorder)
    : INotificationHandler<DomainEventNotification<ExpenseCategoryDeactivated>>
{
    public Task Handle(DomainEventNotification<ExpenseCategoryDeactivated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, BudgetingAudit.ExpenseCategory,
            notification.DomainEvent.ExpenseCategoryId.Value, "Deactivated", cancellationToken);
}

public sealed class ExpenseCategoryDeletedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<ExpenseCategoryDeleted>>
{
    public Task Handle(DomainEventNotification<ExpenseCategoryDeleted> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return BudgetingAudit.RecordAsync(recorder, interceptor, e, BudgetingAudit.ExpenseCategory, e.ExpenseCategoryId.Value, "Deleted",
            [new AuditFieldChange("OperationTypeIds", BudgetingAudit.Join(e.OperationTypeIds), null)], cancellationToken);
    }
}

public sealed class BudgetCreatedAuditHandler(AuditRecorder recorder) : INotificationHandler<DomainEventNotification<BudgetCreated>>
{
    public Task Handle(DomainEventNotification<BudgetCreated> notification, CancellationToken cancellationToken) =>
        recorder.RecordFromChangeTrackerAsync(notification.DomainEvent, BudgetingAudit.Budget, notification.DomainEvent.BudgetId.Value, "Created", cancellationToken);
}

public sealed class BudgetCategoryLimitSetAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<BudgetCategoryLimitSet>>
{
    public Task Handle(DomainEventNotification<BudgetCategoryLimitSet> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return BudgetingAudit.RecordAsync(recorder, interceptor, e, BudgetingAudit.Budget, e.BudgetId.Value, "Updated",
            [new AuditFieldChange(BudgetingAudit.LimitField(e.ExpenseCategoryId), BudgetingAudit.Format(e.OldLimit), BudgetingAudit.Format(e.NewLimit))],
            cancellationToken);
    }
}

public sealed class BudgetCategoryLimitRemovedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<BudgetCategoryLimitRemoved>>
{
    public Task Handle(DomainEventNotification<BudgetCategoryLimitRemoved> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return BudgetingAudit.RecordAsync(recorder, interceptor, e, BudgetingAudit.Budget, e.BudgetId.Value, "Updated",
            [new AuditFieldChange(BudgetingAudit.LimitField(e.ExpenseCategoryId), BudgetingAudit.Format(e.OldLimit), null)], cancellationToken);
    }
}

public sealed class BudgetDeletedAuditHandler(AuditRecorder recorder, DispatchDomainEventsInterceptor interceptor)
    : INotificationHandler<DomainEventNotification<BudgetDeleted>>
{
    public Task Handle(DomainEventNotification<BudgetDeleted> notification, CancellationToken cancellationToken)
    {
        var e = notification.DomainEvent;
        return BudgetingAudit.RecordAsync(recorder, interceptor, e, BudgetingAudit.Budget, e.BudgetId.Value, "Deleted",
            e.Limits.Select(l => new AuditFieldChange(BudgetingAudit.LimitField(l.ExpenseCategoryId), BudgetingAudit.Format(l.Limit), null)),
            cancellationToken);
    }
}

/// <summary>Registration block kept here so AuditModuleExtensions gets a single line.</summary>
internal static class BudgetingAuditRegistration
{
    public static void AddBudgetingAuditHandlers(this IServiceCollection services)
    {
        services.AddScoped<INotificationHandler<DomainEventNotification<ExpenseCategoryCreated>>, ExpenseCategoryCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<ExpenseCategoryUpdated>>, ExpenseCategoryUpdatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<ExpenseCategoryDeactivated>>, ExpenseCategoryDeactivatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<ExpenseCategoryDeleted>>, ExpenseCategoryDeletedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<BudgetCreated>>, BudgetCreatedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<BudgetCategoryLimitSet>>, BudgetCategoryLimitSetAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<BudgetCategoryLimitRemoved>>, BudgetCategoryLimitRemovedAuditHandler>();
        services.AddScoped<INotificationHandler<DomainEventNotification<BudgetDeleted>>, BudgetDeletedAuditHandler>();
    }
}
