using System.Globalization;
using LupexWallet.Reporting.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Reporting.Api;

/// <summary>
/// HTTP-эндпоинты модуля Reporting (docs/api/openapi.yaml, тег Reporting) — вызывается из
/// Host/Program.cs внутри группы /api/v1, уже защищенной RequireAuthorization().
/// UC-19/UC-21 — единственный эндпоинт GET /total-amount, дата опциональна (см. GetTotalAmountQuery).
/// </summary>
public static class ReportingEndpointsExtensions
{
    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reporting").WithTags("Reporting");

        group.MapGet("/total-amount", async (
                ISender sender,
                CancellationToken cancellationToken,
                [FromQuery] DateOnly? date = null) =>
            {
                var dto = await sender.Send(new GetTotalAmountQuery(date), cancellationToken);

                // Null — в системе еще нет ни одного кошелька, значит нет и основного
                // кошелька, задающего валюту отображения (см. GetTotalAmountQuery).
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getTotalAmount");

        return app;
    }

    private static TotalAmountResponse ToResponse(TotalAmountDto dto) => new(
        dto.Date,
        new MoneyResponse(dto.Amount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId),
        dto.RatesAsOfDate,
        dto.ExcludedWallets.Select(w => new ExcludedWalletResponse(w.WalletId, w.Reason)).ToList());
}
