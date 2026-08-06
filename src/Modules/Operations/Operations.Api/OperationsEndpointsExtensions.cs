using System.Globalization;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Operations.Api;

/// <summary>
/// HTTP-эндпоинты модуля Operations (docs/api/openapi.yaml, теги Operations, Transfers).
/// </summary>
public static class OperationsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        MapOperations(app);
        MapTransfers(app);
        return app;
    }

    private static void MapOperations(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/operations").WithTags("Operations");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] Guid? walletId = null, [FromQuery] Guid? operationTypeId = null,
            [FromQuery] DateOnly? dateFrom = null, [FromQuery] DateOnly? dateTo = null,
            [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListOperationsQuery(walletId, operationTypeId, dateFrom, dateTo, cursor, limit), ct);
            return Results.Ok(ToResponse(page));
        }).WithName("listOperations");

        group.MapPost("/", async ([FromBody] OperationCreateRequest request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var command = new CreateOperationCommand(
                    request.WalletId, request.OperationTypeId, request.Amount, request.OperationDate,
                    ParseAdjustmentMode(request.AdjustmentMode));
                var dto = await sender.Send(command, ct);
                var response = ToResponse(dto);
                return Results.Created($"/api/v1/operations/{response.Id}", response);
            }
            catch (OperationsDomainException ex)
            {
                return ValidationProblem(ex);
            }
        }).WithName("createOperation");

        group.MapGet("/{operationId:guid}", async (Guid operationId, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new GetOperationQuery(operationId), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        }).WithName("getOperation");

        group.MapPatch("/{operationId:guid}", async (Guid operationId, [FromBody] OperationUpdateRequest request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var command = new UpdateOperationCommand(
                    operationId, request.WalletId, request.OperationTypeId, request.Amount, request.OperationDate,
                    ParseAdjustmentMode(request.AdjustmentMode));
                var dto = await sender.Send(command, ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OperationPartOfTransferException ex)
            {
                // W2.6: унификация с DELETE /operations/{id} — тот же домен-конфликт (операция —
                // часть перевода) должен давать тот же HTTP-код для обеих операций.
                return ConflictProblem(ex);
            }
            catch (OperationsDomainException ex)
            {
                return ValidationProblem(ex);
            }
        }).WithName("updateOperation");

        group.MapDelete("/{operationId:guid}", async (Guid operationId, ISender sender, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(new DeleteOperationCommand(operationId), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OperationsDomainException ex) when (ex is OperationPartOfTransferException or OperationDeletionNotAllowedException)
            {
                return ConflictProblem(ex);
            }
        }).WithName("deleteOperation");
    }

    private static void MapTransfers(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/transfers").WithTags("Transfers");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] Guid? walletId = null, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListTransfersQuery(walletId, cursor, limit), ct);
            return Results.Ok(ToResponse(page));
        }).WithName("listTransfers");

        group.MapPost("/", async ([FromBody] TransferCreateRequest request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var command = new CreateTransferCommand(request.SourceWalletId, request.TargetWalletId, request.Amount, request.TransferDate);
                var dto = await sender.Send(command, ct);
                var response = ToResponse(dto);
                return Results.Created($"/api/v1/transfers/{response.Id}", response);
            }
            catch (OperationsDomainException ex)
            {
                return ex is TransferSameWalletException or TransferCurrencyMismatchException or NoActiveOperationTypeForTransferException
                    ? ConflictProblem(ex)
                    : ValidationProblem(ex);
            }
        }).WithName("createTransfer");

        group.MapGet("/{transferId:guid}", async (Guid transferId, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new GetTransferQuery(transferId), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        }).WithName("getTransfer");

        group.MapDelete("/{transferId:guid}", async (Guid transferId, ISender sender, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(new DeleteTransferCommand(transferId), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (TransferDeletionNotAllowedException ex)
            {
                return ConflictProblem(ex);
            }
        }).WithName("deleteTransfer");
    }

    private static AdjustmentMode? ParseAdjustmentMode(string? value) =>
        string.IsNullOrEmpty(value) ? null : Enum.Parse<AdjustmentMode>(value, ignoreCase: true);

    private static IResult ValidationProblem(OperationsDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Operation validation failed",
        detail: ex.Message,
        type: "https://lupexwallet/errors/operation-validation-error");

    private static IResult ConflictProblem(OperationsDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Operation conflict",
        detail: ex.Message,
        type: "https://lupexwallet/errors/operation-conflict");

    private static OperationResponse ToResponse(OperationDto dto) => new(
        dto.Id, dto.WalletId, dto.OperationTypeId, new MoneyResponse(dto.Amount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId),
        dto.OperationDate, dto.AdjustmentMode, dto.TransferId, dto.CreatedAt, dto.UpdatedAt);

    private static OperationPageResponse ToResponse(OperationPageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));

    private static TransferResponse ToResponse(TransferDto dto) => new(
        dto.Id, dto.SourceWalletId, dto.TargetWalletId, dto.SourceOperationId, dto.TargetOperationId,
        new MoneyResponse(dto.Amount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId), dto.TransferDate, dto.CreatedAt);

    private static TransferPageResponse ToResponse(TransferPageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));
}
