using System.Globalization;
using LupexWallet.ExchangeRates.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.ExchangeRates.Api;

/// <summary>
/// HTTP-эндпоинты модуля ExchangeRates (docs/api/openapi.yaml, тег ExchangeRates) —
/// вызывается из Host/Program.cs внутри группы /api/v1, уже защищенной RequireAuthorization().
/// </summary>
public static class ExchangeRatesEndpointsExtensions
{
    public static IEndpointRouteBuilder MapExchangeRatesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/exchange-rates").WithTags("ExchangeRates");

        group.MapGet("/latest", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var dto = await sender.Send(new GetLatestExchangeRatesQuery(), cancellationToken);
                return Results.Ok(ToResponse(dto));
            })
            .WithName("getLatestExchangeRates");

        group.MapPost("/refresh", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var result = await sender.Send(new RefreshExchangeRatesCommand(IsManualTrigger: true), cancellationToken);
                var response = ToResponse(result);

                // openapi.yaml: 502 при недоступности Frankfurter, тело — те же
                // LatestExchangeRates (прежние курсы, lastSuccessfulUpdate не изменился).
                return result.HadFailures
                    ? Results.Json(response, statusCode: StatusCodes.Status502BadGateway)
                    : Results.Ok(response);
            })
            .WithName("refreshExchangeRates");

        return app;
    }

    private static LatestExchangeRatesResponse ToResponse(LatestExchangeRatesDto dto) => new(
        dto.LastSuccessfulUpdate,
        dto.Rates.Select(ToResponse).ToList());

    private static LatestExchangeRatesResponse ToResponse(RefreshExchangeRatesResult result) => new(
        result.LastSuccessfulUpdate,
        result.Rates.Select(ToResponse).ToList());

    private static ExchangeRateQuoteResponse ToResponse(ExchangeRateQuoteDto dto) =>
        new(dto.FromCurrencyId, dto.ToCurrencyId, dto.Rate.ToString(CultureInfo.InvariantCulture));
}
