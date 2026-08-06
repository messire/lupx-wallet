using Microsoft.AspNetCore.Routing;

namespace LupexWallet.ReferenceData.Api;

/// <summary>
/// Точка композиции HTTP-эндпоинтов модуля ReferenceData — вызывается из Host/Program.cs.
/// TODO: наполняется по мере реализации вертикальных срезов (см. docs/api/openapi.yaml,
/// теги "ReferenceData" соответствующих ресурсов).
/// </summary>
public static class ReferenceDataEndpointsExtensions
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
