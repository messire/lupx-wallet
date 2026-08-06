namespace LupexWallet.Audit.Api;

// DTO по контракту docs/api/openapi.yaml (схемы AuditEntry, AuditEntryPage, AuditFieldChange).

public sealed record AuditFieldChangeResponse(string Field, string? OldValue, string? NewValue);

public sealed record AuditEntryResponse(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    DateTimeOffset OccurredAt,
    string ActorKind,
    string? ActorSystemProcess,
    IReadOnlyList<AuditFieldChangeResponse> Changes);

public sealed record CursorPageMeta(string? NextCursor, bool HasMore);

public sealed record AuditEntryPageResponse(IReadOnlyList<AuditEntryResponse> Data, CursorPageMeta Pagination);
