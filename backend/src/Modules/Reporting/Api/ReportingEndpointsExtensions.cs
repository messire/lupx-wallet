using System.Globalization;
using LupexWallet.Reporting.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Reporting.Api;

/// <summary>
/// HTTP endpoints of the Reporting module (docs/api/openapi.yaml, Reporting tag) — called from
/// Host/Program.cs inside the /api/v1 group, already protected by RequireAuthorization().
/// UC-19/UC-21 — the single GET /total-amount endpoint, date is optional (see GetTotalAmountQuery).
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
