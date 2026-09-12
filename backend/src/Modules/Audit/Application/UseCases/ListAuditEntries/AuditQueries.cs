using LupexWallet.Audit.Domain;
using LupexWallet.SharedKernel;
using MediatR;

namespace LupexWallet.Audit.Application;

public sealed record AuditFieldChangeDto(string Field, string? OldValue, string? NewValue)
{
    public static AuditFieldChangeDto FromDomain(AuditFieldChange change) => new(change.FieldName, change.OldValue, change.NewValue);
}

public sealed record AuditEntryDto(
    Guid Id,
    string EntityType,
    Guid EntityId,
    string Action,
    DateTimeOffset OccurredAt,
    AuditActorKind ActorKind,
    string? ActorSystemProcess,
    IReadOnlyList<AuditFieldChangeDto> Changes)
{
    public static AuditEntryDto FromDomain(AuditEntry entry) => new(
        entry.Id.Value,
        entry.EntityType,
        entry.EntityId,
        entry.Action,
        entry.OccurredAt,
        entry.Actor.Kind,
        entry.Actor.SystemProcessName,
        entry.Changes.Select(AuditFieldChangeDto.FromDomain).ToList());
}

public sealed record AuditEntryPageDto(IReadOnlyList<AuditEntryDto> Data, string? NextCursor, bool HasMore);

/// <summary>UC-25 (docs/api/openapi.yaml: GET /audit-entries).</summary>
public sealed record ListAuditEntriesQuery(string EntityType, Guid EntityId, string? Cursor, int Limit)
    : IRequest<AuditEntryPageDto>, IQuery<AuditEntryPageDto>;

public sealed class ListAuditEntriesQueryHandler(IAuditEntryRepository repository)
    : IRequestHandler<ListAuditEntriesQuery, AuditEntryPageDto>
{
    public async Task<AuditEntryPageDto> Handle(ListAuditEntriesQuery request, CancellationToken cancellationToken)
    {
        var cursor = AuditEntryCursor.TryDecode(request.Cursor);
        var limit = Math.Clamp(request.Limit <= 0 ? 20 : request.Limit, 1, 100);

        var page = await repository.ListAsync(request.EntityType, request.EntityId, cursor, limit, cancellationToken);

        var items = page.Items.Select(AuditEntryDto.FromDomain).ToList();
        var nextCursor = page.Items.Count > 0
            ? new AuditEntryCursor(page.Items[^1].OccurredAt, page.Items[^1].Id.Value).Encode()
            : null;

        return new AuditEntryPageDto(items, page.HasMore ? nextCursor : null, page.HasMore);
    }
}
