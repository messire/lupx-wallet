namespace LupexWallet.Audit.Domain;

/// <summary>VO of a single changed field (ddd-model.md, §4) — audit log representation for one record.</summary>
public sealed record AuditFieldChange(string FieldName, string? OldValue, string? NewValue);
