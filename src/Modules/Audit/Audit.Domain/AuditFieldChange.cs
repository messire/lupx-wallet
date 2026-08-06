namespace LupexWallet.Audit.Domain;

/// <summary>VO одного изменённого поля (ddd-model.md, §4) — представление для журнала аудита конкретной записи.</summary>
public sealed record AuditFieldChange(string FieldName, string? OldValue, string? NewValue);
