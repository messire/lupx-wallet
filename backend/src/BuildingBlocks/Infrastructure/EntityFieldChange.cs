namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// A single changed scalar property of an entity, captured from the EF Core ChangeTracker
/// (see DispatchDomainEventsInterceptor, ADR-0010). Does not depend on the Audit module —
/// a shared infrastructure type usable by any future subscriber that needs an old/new
/// diff, not only Audit.
/// </summary>
public sealed record EntityFieldChange(string FieldName, string? OldValue, string? NewValue);
