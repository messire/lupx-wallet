using System.Globalization;
using LupexWallet.Operations.Application;
using LupexWallet.Operations.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Operations.Api;

/// <summary>HTTP endpoints of the Operations module (docs/api/openapi.yaml, tags Operations, Transfers).</summary>
public static class OperationsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapOperationsEndpoints(this IEndpointRouteBuilder app)
    {
        MapOperations(app);
        MapTransfers(app);
        MapStatedBalances(app);
        return app;
    }

    private static void MapStatedBalances(IEndpointRouteBuilder app)
    {
        app.MapPut("/wallets/{walletId:guid}/stated-balances/{date}", async (
            Guid walletId, DateOnly date, [FromBody] StatedBalanceRequest request, ISender sender, CancellationToken ct) =>
        {
            if (request.Amount is not { } amount)
            {
                return Results.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Operation validation failed",
                    detail: "Укажите баланс на конец дня.",
                    type: "https://lupexwallet/errors/operation-validation-error");
            }

            try
            {
                var result = await sender.Send(new SetStatedBalanceCommand(walletId, date, amount), ct);
                var response = ToResponse(result.Operation);
                return result.Created
                    ? Results.Created($"/api/v1/operations/{response.Id}", response)
                    : Results.Ok(response);
            }
            catch (WalletReferenceNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OperationsDomainException ex) when (IsLifecycleConflict(ex))
            {
                return ConflictProblem(ex);
            }
            catch (OperationsDomainException ex)
            {
                return ValidationProblem(ex);
            }
        }).WithTags("Operations").WithName("setStatedBalance");
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
                // A rejected mode travels with the command, so 404 and 409 (correction, deleted) win over this 400.
                var adjustmentMode = TryParseAdjustmentMode(request.AdjustmentMode, out var adjustmentModeError);
                var command = new UpdateOperationCommand(
                    operationId, request.WalletId, request.OperationTypeId, request.Amount, request.OperationDate,
                    adjustmentMode, adjustmentModeError);
                var dto = await sender.Send(command, ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OperationsDomainException ex) when (IsLifecycleConflict(ex))
            {
                // Part of a transfer, soft-deleted (restore first) or changed concurrently — same 409 as DELETE/restore.
                return ConflictProblem(ex, $"/api/v1/operations/{operationId}");
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
            catch (OperationsDomainException ex) when (IsLifecycleConflict(ex))
            {
                return ConflictProblem(ex, $"/api/v1/operations/{operationId}");
            }
        }).WithName("deleteOperation");

        group.MapPost("/{operationId:guid}/restore", async (Guid operationId, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new RestoreOperationCommand(operationId), ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OperationsDomainException ex) when (IsLifecycleConflict(ex))
            {
                return ConflictProblem(ex, $"/api/v1/operations/{operationId}");
            }
        }).WithName("restoreOperation");
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
            catch (OperationsDomainException ex) when (IsLifecycleConflict(ex))
            {
                return ConflictProblem(ex, $"/api/v1/transfers/{transferId}");
            }
        }).WithName("deleteTransfer");

        group.MapPost("/{transferId:guid}/restore", async (Guid transferId, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new RestoreTransferCommand(transferId), ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (OperationsDomainException ex) when (IsLifecycleConflict(ex))
            {
                return ConflictProblem(ex, $"/api/v1/transfers/{transferId}");
            }
        }).WithName("restoreTransfer");
    }

    /// <summary>
    /// Soft-delete lifecycle violations (ADR-0002, §3) and concurrent modification — all answered
    /// with 409. TransferLegMismatchException is deliberately absent: it signals corrupted data (500).
    /// </summary>
    private static bool IsLifecycleConflict(OperationsDomainException ex) => ex is
        OperationPartOfTransferException or OperationAlreadyDeletedException or OperationNotDeletedException
        or OperationIsDeletedException or TransferAlreadyDeletedException or TransferNotDeletedException
        or CorrectionNotEditableException or RecordModifiedConcurrentlyException;

    /// <summary>
    /// Absolute is recognized before enum binding and answered with 400: the balance for a date is
    /// stated through a separate action (ADR-0003, §6).
    /// </summary>
    private static AdjustmentMode? ParseAdjustmentMode(string? value)
    {
        var mode = TryParseAdjustmentMode(value, out var error);
        return error is null ? mode : throw error;
    }

    /// <summary>
    /// Only the names of <see cref="AdjustmentMode"/> are accepted (case-insensitive): numeric
    /// strings that <c>Enum.Parse</c> would map to a member or an undefined value are rejected.
    /// </summary>
    private static AdjustmentMode? TryParseAdjustmentMode(string? value, out OperationsDomainException? error)
    {
        error = null;
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        if (string.Equals(value, "Absolute", StringComparison.OrdinalIgnoreCase))
        {
            error = new AbsoluteAdjustmentNotSupportedException();
            return null;
        }

        foreach (var mode in Enum.GetValues<AdjustmentMode>())
        {
            if (string.Equals(mode.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                return mode;
            }
        }

        error = new AdjustmentModeUnknownException();
        return null;
    }

    private static IResult ValidationProblem(OperationsDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Operation validation failed",
        detail: ex.Message,
        type: "https://lupexwallet/errors/operation-validation-error");

    private static IResult ConflictProblem(OperationsDomainException ex, string? instance = null) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Operation conflict",
        detail: ex.Message,
        instance: instance,
        type: "https://lupexwallet/errors/operation-conflict");

    private static OperationResponse ToResponse(OperationDto dto) => new(
        dto.Id, dto.WalletId, dto.OperationTypeId, new MoneyResponse(dto.Amount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId),
        dto.OperationDate, dto.AdjustmentMode, dto.IsCorrection,
        dto.StatedBalanceAmount is { } stated ? new MoneyResponse(stated.ToString(CultureInfo.InvariantCulture), dto.CurrencyId) : null,
        dto.TransferId, dto.CreatedAt, dto.UpdatedAt, dto.IsDeleted, dto.DeletedAt);

    private static OperationPageResponse ToResponse(OperationPageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));

    private static TransferResponse ToResponse(TransferDto dto) => new(
        dto.Id, dto.SourceWalletId, dto.TargetWalletId, dto.SourceOperationId, dto.TargetOperationId,
        new MoneyResponse(dto.Amount.ToString(CultureInfo.InvariantCulture), dto.CurrencyId), dto.TransferDate, dto.CreatedAt, dto.IsDeleted, dto.DeletedAt);

    private static TransferPageResponse ToResponse(TransferPageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));
}
