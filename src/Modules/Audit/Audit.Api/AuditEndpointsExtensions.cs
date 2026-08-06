using LupexWallet.Audit.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Audit.Api;

/// <summary>HTTP-эндпоинты модуля Audit (docs/api/openapi.yaml, тег Audit, UC-25).</summary>
public static class AuditEndpointsExtensions
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/audit-entries", async (
            [FromQuery] string entityType,
            [FromQuery] Guid entityId,
            ISender sender,
            CancellationToken ct,
            [FromQuery] string? cursor = null,
            [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListAuditEntriesQuery(entityType, entityId, cursor, limit), ct);
            return Results.Ok(ToResponse(page));
        }).WithTags("Audit").WithName("listAuditEntries");

        return app;
    }

    private static AuditEntryResponse ToResponse(AuditEntryDto dto) => new(
        dto.Id,
        dto.EntityType,
        dto.EntityId,
        dto.Action,
        dto.OccurredAt,
        dto.ActorKind.ToString(),
        dto.ActorSystemProcess,
        dto.Changes.Select(c => new AuditFieldChangeResponse(c.Field, c.OldValue, c.NewValue)).ToList());

    private static AuditEntryPageResponse ToResponse(AuditEntryPageDto page) =>
        new(page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));
}
