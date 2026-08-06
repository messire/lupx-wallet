using Microsoft.AspNetCore.Routing;

namespace LupexWallet.ExchangeRates.Api;

/// <summary>
/// Точка композиции HTTP-эндпоинтов модуля ExchangeRates — вызывается из Host/Program.cs.
/// TODO: наполняется по мере реализации вертикальных срезов (см. docs/api/openapi.yaml,
/// теги "ExchangeRates" соответствующих ресурсов).
/// </summary>
public static class ExchangeRatesEndpointsExtensions
{
    public static IEndpointRouteBuilder MapExchangeRatesEndpoints(this IEndpointRouteBuilder app)
    {
        return app;
    }
}
