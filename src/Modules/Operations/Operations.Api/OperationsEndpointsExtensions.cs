using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Operations.Api;

/// <summary>
/// Точка композиции HTTP-эндпоинтов модуля Operations — вызывается из Host/Program.cs.
/// TODO: наполняется по мере реализации вертикальных срезов (см. docs/api/openapi.yaml,
/// теги "Operations" соответствующих ресурсов).
/// </summary>
public static class OperationsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
