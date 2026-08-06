using System.Globalization;
using LupexWallet.BalanceHistory.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.BalanceHistory.Api;

/// <summary>HTTP-эндпоинты модуля BalanceHistory (docs/api/openapi.yaml, тег BalanceHistory).</summary>
public static class BalanceHistoryEndpointsExtensions
{
    public static IEndpointRouteBuilder MapBalanceHistoryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wallets/{walletId:guid}").WithTags("BalanceHistory");

        group.MapGet("/balance", async (Guid walletId, ISender sender, CancellationToken ct, [FromQuery] DateOnly? date = null) =>
        {
            var dto = await sender.Send(new GetWalletBalanceQuery(walletId, date), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        }).WithName("getWalletBalance");

        group.MapGet("/balance-history", async (
            Guid walletId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, ISender sender, CancellationToken ct,
            [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            if (from > to)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid date range",
                    detail: $"'from' ({from:yyyy-MM-dd}) должен быть не позже 'to' ({to:yyyy-MM-dd}).",
                    type: "https://lupexwallet/errors/balance-history-invalid-range");
            }

            var page = await sender.Send(new ListWalletBalanceHistoryQuery(walletId, from, to, cursor, limit), ct);
            return page is null ? Results.NotFound() : Results.Ok(ToResponse(page));
        }).WithName("listWalletBalanceHistory");

        return app;
    }

    private static BalanceSnapshotResponse ToResponse(BalanceSnapshotDto dto) =>
        new(dto.WalletId, dto.Date, new MoneyResponse(dto.BalanceAmount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId));

    private static BalanceSnapshotPageResponse ToResponse(BalanceSnapshotPageDto page) =>
        new(page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));
}
