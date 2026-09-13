namespace LupexWallet.BuildingBlocks.Infrastructure;

/// <summary>
/// Один изменённый скаляр сущности, снятый из EF Core ChangeTracker
/// (см. DispatchDomainEventsInterceptor, ADR-0010). Не зависит от модуля Audit —
/// общий инфраструктурный тип, которым может воспользоваться любой будущий подписчик,
/// которому нужен diff old/new, а не только Audit.
/// </summary>
public sealed record EntityFieldChange(string FieldName, string? OldValue, string? NewValue);
