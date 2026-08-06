using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Wallets.Api;

/// <summary>
/// HTTP-эндпоинты модуля Wallets (docs/api/openapi.yaml, тег Wallets) — вызывается из
/// Host/Program.cs внутри группы /api/v1, уже защищенной RequireAuthorization().
/// В этом срезе реализованы только создание и список кошельков (UC-01, UC-07);
/// остальные эндпоинты контракта (archive, set-primary, currency, delete, balance)
/// — предмет последующих срезов.
/// </summary>
public static class WalletsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapWalletsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wallets").WithTags("Wallets");

        group.MapGet("/", async (
                ISender sender,
                CancellationToken cancellationToken,
                [FromQuery] bool includeArchived = false,
                [FromQuery] string? cursor = null,
                [FromQuery] int limit = 20) =>
            {
                var result = await sender.Send(
                    new ListWalletsQuery(includeArchived, cursor, limit == 0 ? 20 : limit),
                    cancellationToken);

                return Results.Ok(ToResponse(result));
            })
            .WithName("listWallets");

        group.MapPost("/", async (
                [FromBody] WalletCreateRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var command = new CreateWalletCommand(
                        request.Name,
                        request.WalletTypeId,
                        request.CurrencyId,
                        request.InitialBalanceAmount,
                        request.AccountingStartDate,
                        request.PurposeDescription,
                        request.IncludeInTotal ?? true,
                        request.DisplayOrder ?? 0,
                        request.Color,
                        request.Icon);

                    var dto = await sender.Send(command, cancellationToken);
                    var response = ToResponse(dto);
                    return Results.Created($"/api/v1/wallets/{response.Id}", response);
                }
                catch (WalletDomainException ex)
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status400BadRequest,
                        title: "Wallet validation failed",
                        detail: ex.Message,
                        type: "https://lupexwallet/errors/wallet-validation-error");
                }
            })
            .WithName("createWallet");

        return app;
    }

    private static WalletPageResponse ToResponse(WalletPageDto page) => new(
        page.Data.Select(ToResponse).ToList(),
        new CursorPageMeta(page.NextCursor, page.HasMore));

    private static WalletResponse ToResponse(WalletDto dto) => new(
        dto.Id,
        dto.Name,
        dto.WalletTypeId,
        dto.PurposeDescription,
        dto.CurrencyId,
        new MoneyResponse(dto.InitialBalanceAmount.ToString("G29"), dto.CurrencyId),
        dto.AccountingStartDate,
        new MoneyResponse(dto.CurrentBalanceAmount.ToString("G29"), dto.CurrencyId),
        dto.IncludeInTotal,
        dto.IsPrimary,
        dto.IsArchived,
        dto.DisplayOrder,
        dto.Color,
        dto.Icon,
        dto.CreatedAt,
        dto.UpdatedAt);
}
