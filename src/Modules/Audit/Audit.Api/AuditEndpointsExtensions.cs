using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Audit.Api;

/// <summary>
/// Точка композиции HTTP-эндпоинтов модуля Audit — вызывается из Host/Program.cs.
/// TODO: наполняется по мере реализации вертикальных срезов (см. docs/api/openapi.yaml,
/// теги "Audit" соответствующих ресурсов).
/// </summary>
public static class AuditEndpointsExtensions
{
    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
