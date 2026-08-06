using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Reporting.Api;

/// <summary>
/// Точка композиции HTTP-эндпоинтов модуля Reporting — вызывается из Host/Program.cs.
/// TODO: наполняется по мере реализации вертикальных срезов (см. docs/api/openapi.yaml,
/// теги "Reporting" соответствующих ресурсов).
/// </summary>
public static class ReportingEndpointsExtensions
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
