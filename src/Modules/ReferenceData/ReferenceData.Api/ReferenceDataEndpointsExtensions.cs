using LupexWallet.ReferenceData.Application;
using LupexWallet.ReferenceData.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.ReferenceData.Api;

/// <summary>
/// HTTP-эндпоинты модуля ReferenceData (docs/api/openapi.yaml, теги WalletTypes,
/// OperationTypes, Currencies, OperationBehaviorKinds).
/// </summary>
public static class ReferenceDataEndpointsExtensions
{
    public static IEndpointRouteBuilder MapReferenceDataEndpoints(this IEndpointRouteBuilder app)
    {
        MapWalletTypes(app);
        MapOperationTypes(app);
        MapCurrencies(app);
        MapOperationBehaviorKinds(app);
        return app;
    }

    private static void MapWalletTypes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/wallet-types").WithTags("WalletTypes");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] bool includeInactive = false, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListWalletTypesQuery(includeInactive, cursor, limit), ct);
            return Results.Ok(ToResponse(page));
        }).WithName("listWalletTypes");

        group.MapPost("/", async ([FromBody] ReferenceItemCreateRequest request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new CreateWalletTypeCommand(request.Name), ct);
                var response = ToResponse(dto);
                return Results.Created($"/api/v1/wallet-types/{response.Id}", response);
            }
            catch (ReferenceDataDomainException ex)
            {
                return ValidationProblem(ex);
            }
        }).WithName("createWalletType");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new DeactivateWalletTypeCommand(id), ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("deactivateWalletType");

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(new DeleteWalletTypeCommand(id), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ReferenceItemInUseException ex)
            {
                return ConflictProblem(ex);
            }
        }).WithName("deleteWalletType");
    }

    private static void MapOperationTypes(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/operation-types").WithTags("OperationTypes");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] bool includeInactive = false, [FromQuery] Guid? behaviorKindId = null,
            [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListOperationTypesQuery(includeInactive, behaviorKindId, cursor, limit), ct);
            return Results.Ok(ToResponse(page));
        }).WithName("listOperationTypes");

        group.MapPost("/", async ([FromBody] OperationTypeCreateRequest request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new CreateOperationTypeCommand(request.Name, request.BehaviorKindId), ct);
                var response = ToResponse(dto);
                return Results.Created($"/api/v1/operation-types/{response.Id}", response);
            }
            catch (ReferenceDataDomainException ex)
            {
                return ValidationProblem(ex);
            }
        }).WithName("createOperationType");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new DeactivateOperationTypeCommand(id), ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("deactivateOperationType");

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(new DeleteOperationTypeCommand(id), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ReferenceItemInUseException ex)
            {
                return ConflictProblem(ex);
            }
        }).WithName("deleteOperationType");
    }

    private static void MapCurrencies(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/currencies").WithTags("Currencies");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] bool includeInactive = false, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListCurrenciesQuery(includeInactive, cursor, limit), ct);
            return Results.Ok(ToResponse(page));
        }).WithName("listCurrencies");

        group.MapPost("/", async ([FromBody] CurrencyCreateRequest request, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new CreateCurrencyCommand(request.Code, request.Name), ct);
                var response = ToResponse(dto);
                return Results.Created($"/api/v1/currencies/{response.Id}", response);
            }
            catch (CurrencyCodeAlreadyExistsException ex)
            {
                return ConflictProblem(ex);
            }
            catch (ReferenceDataDomainException ex)
            {
                return ValidationProblem(ex);
            }
        }).WithName("createCurrency");

        group.MapPost("/{id:guid}/deactivate", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                var dto = await sender.Send(new DeactivateCurrencyCommand(id), ct);
                return Results.Ok(ToResponse(dto));
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
        }).WithName("deactivateCurrency");

        group.MapDelete("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            try
            {
                await sender.Send(new DeleteCurrencyCommand(id), ct);
                return Results.NoContent();
            }
            catch (KeyNotFoundException)
            {
                return Results.NotFound();
            }
            catch (ReferenceItemInUseException ex)
            {
                return ConflictProblem(ex);
            }
        }).WithName("deleteCurrency");
    }

    private static void MapOperationBehaviorKinds(IEndpointRouteBuilder app)
    {
        app.MapGet("/operation-behavior-kinds", async (ISender sender, CancellationToken ct) =>
        {
            var kinds = await sender.Send(new ListOperationBehaviorKindsQuery(), ct);
            return Results.Ok(kinds.Select(k => new OperationBehaviorKindResponse(k.Id, k.Code, k.Name)));
        }).WithTags("OperationBehaviorKinds").WithName("listOperationBehaviorKinds");
    }

    private static IResult ValidationProblem(ReferenceDataDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest,
        title: "Reference data validation failed",
        detail: ex.Message,
        type: "https://lupexwallet/errors/reference-data-validation-error");

    private static IResult ConflictProblem(ReferenceDataDomainException ex) => Results.Problem(
        statusCode: StatusCodes.Status409Conflict,
        title: "Reference data conflict",
        detail: ex.Message,
        type: "https://lupexwallet/errors/reference-data-conflict");

    private static ReferenceItemResponse ToResponse(WalletTypeDto dto) => new(dto.Id, dto.Name, dto.IsActive);

    private static ReferenceItemPageResponse ToResponse(WalletTypePageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));

    private static OperationTypeResponse ToResponse(OperationTypeDto dto) => new(dto.Id, dto.Name, dto.BehaviorKindId, dto.IsActive);

    private static OperationTypePageResponse ToResponse(OperationTypePageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));

    private static CurrencyResponse ToResponse(CurrencyDto dto) => new(dto.Id, dto.Code, dto.Name, dto.IsActive);

    private static CurrencyPageResponse ToResponse(CurrencyPageDto page) => new(
        page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore));
}
