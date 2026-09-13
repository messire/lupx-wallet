using System.Globalization;
using LupexWallet.Wallets.Application;
using LupexWallet.Wallets.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Wallets.Api;

/// <summary>
/// HTTP endpoints of the Wallets module (docs/api/openapi.yaml, tag Wallets) — mapped from
/// Host/Program.cs inside the /api/v1 group, already protected by RequireAuthorization().
/// UC-01…UC-07 are implemented here; balance/balance-history live in BalanceHistory (tag
/// BalanceHistory in openapi.yaml).
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

        group.MapGet("/{walletId:guid}", async (Guid walletId, ISender sender, CancellationToken cancellationToken) =>
            {
                var dto = await sender.Send(new GetWalletQuery(walletId), cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getWallet");

        group.MapPatch("/{walletId:guid}", async (
                Guid walletId,
                [FromBody] WalletUpdateRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var command = new UpdateWalletCommand(
                        walletId,
                        request.Name,
                        request.WalletTypeId,
                        request.PurposeDescription,
                        request.IncludeInTotal,
                        request.DisplayOrder,
                        request.Color,
                        request.Icon);

                    var dto = await sender.Send(command, cancellationToken);
                    return Results.Ok(ToResponse(dto));
                }
                catch (WalletNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (WalletDomainException ex)
                {
                    return ValidationProblem(ex);
                }
            })
            .WithName("updateWallet");

        group.MapPost("/{walletId:guid}/archive", async (Guid walletId, ISender sender, CancellationToken cancellationToken) =>
            {
                try
                {
                    var dto = await sender.Send(new ArchiveWalletCommand(walletId), cancellationToken);
                    return Results.Ok(ToResponse(dto));
                }
                catch (WalletNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (CannotArchivePrimaryWalletException ex)
                {
                    return ConflictProblem(ex);
                }
            })
            .WithName("archiveWallet");

        group.MapPost("/{walletId:guid}/set-primary", async (Guid walletId, ISender sender, CancellationToken cancellationToken) =>
            {
                try
                {
                    var dto = await sender.Send(new SetPrimaryWalletCommand(walletId), cancellationToken);
                    return Results.Ok(ToResponse(dto));
                }
                catch (WalletNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (CannotSetArchivedWalletAsPrimaryException ex)
                {
                    return ConflictProblem(ex);
                }
            })
            .WithName("setPrimaryWallet");

        group.MapPut("/{walletId:guid}/currency", async (
                Guid walletId,
                [FromBody] WalletChangeCurrencyRequest request,
                ISender sender,
                CancellationToken cancellationToken) =>
            {
                try
                {
                    var dto = await sender.Send(new ChangeWalletCurrencyCommand(walletId, request.CurrencyId), cancellationToken);
                    return Results.Ok(ToResponse(dto));
                }
                catch (WalletNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (WalletCurrencyChangeNotAllowedException ex)
                {
                    return ConflictProblem(ex);
                }
            })
            .WithName("changeWalletCurrency");

        group.MapDelete("/{walletId:guid}", async (Guid walletId, ISender sender, CancellationToken cancellationToken) =>
            {
                try
                {
                    await sender.Send(new DeleteWalletCommand(walletId), cancellationToken);
                    return Results.NoContent();
                }
                catch (WalletNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (WalletDomainException ex) when (ex is WalletDeletionNotAllowedException or CannotDeletePrimaryWalletException)
                {
                    return ConflictProblem(ex);
                }
            })
            .WithName("deleteWallet");

        return app;
    }

    private static IResult ValidationProblem(WalletDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Wallet validation failed",
        detail: ex.Message,
        type: "https://lupexwallet/errors/wallet-validation-error");

    private static IResult ConflictProblem(WalletDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Wallet conflict",
        detail: ex.Message,
        type: "https://lupexwallet/errors/wallet-conflict");

    private static WalletPageResponse ToResponse(WalletPageDto page) => new(
        page.Data.Select(ToResponse).ToList(),
        new CursorPageMeta(page.NextCursor, page.HasMore));

    private static WalletResponse ToResponse(WalletDto dto) => new(
        dto.Id,
        dto.Name,
        dto.WalletTypeId,
        dto.PurposeDescription,
        dto.CurrencyId,
        new MoneyResponse(dto.InitialBalanceAmount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId),
        dto.AccountingStartDate,
        new MoneyResponse(dto.CurrentBalanceAmount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId),
        dto.IncludeInTotal,
        dto.IsPrimary,
        dto.IsArchived,
        dto.DisplayOrder,
        dto.Color,
        dto.Icon,
        dto.CreatedAt,
        dto.UpdatedAt);
}
