using Microsoft.AspNetCore.Routing;

namespace LupexWallet.BalanceHistory.Api;

/// <summary>
/// Точка композиции HTTP-эндпоинтов модуля BalanceHistory — вызывается из Host/Program.cs.
/// TODO: наполняется по мере реализации вертикальных срезов (см. docs/api/openapi.yaml,
/// теги "BalanceHistory" соответствующих ресурсов).
/// </summary>
public static class BalanceHistoryEndpointsExtensions
{
    public static IEndpointRouteBuilder MapBalanceHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
