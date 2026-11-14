using System.Globalization;
using LupexWallet.Planning.Application;
using LupexWallet.Planning.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Planning.Api;

/// <summary>
/// HTTP endpoints of the Planning module (docs/api/openapi.yaml, tag Planning; ADR-0010 §6).
/// Errors: PlanningNotFoundException → 404, PlanningConflictException → 409, other PlanningDomainException → 400.
/// </summary>
public static class PlanningEndpointsExtensions
{
    private const string ValidationErrorType = "https://lupexwallet/errors/planning-validation-error";
    private const string ConflictErrorType = "https://lupexwallet/errors/planning-conflict";

    public static IEndpointRouteBuilder MapPlanningEndpoints(this IEndpointRouteBuilder app)
    {
        MapPlannedTransactions(app);
        MapOccurrences(app);
        return app;
    }

    private static void MapPlannedTransactions(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/planned-transactions").WithTags("Planning");

        group.MapGet("/", (
            ISender sender, CancellationToken ct,
            [FromQuery] string? status = null, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) => Handle(async () =>
        {
            PlannedTransactionStatus? statusFilter = status is null ? null : ParseName<PlannedTransactionStatus>(status, "status");
            var page = await sender.Send(new ListPlannedTransactionsQuery(statusFilter, cursor, limit), ct);
            return Results.Ok(new PlannedTransactionPageResponse(
                page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore)));
        })).WithName("listPlannedTransactions");

        group.MapPost("/", ([FromBody] PlannedTransactionWriteRequest request, ISender sender, CancellationToken ct) => Handle(async () =>
        {
            var dto = await sender.Send(new CreatePlannedTransactionCommand(
                request.OperationTypeId, RequireAmount(request.Amount), request.CurrencyId, request.WalletId,
                request.Description, request.StartDate, ToInput(request.Recurrence)), ct);
            var response = ToResponse(dto);
            return Results.Created($"/api/v1/planned-transactions/{response.Id}", response);
        })).WithName("createPlannedTransaction");

        group.MapGet("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Handle(async () =>
        {
            var dto = await sender.Send(new GetPlannedTransactionQuery(id), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        })).WithName("getPlannedTransaction");

        group.MapPatch("/{id:guid}", (Guid id, [FromBody] PlannedTransactionWriteRequest request, ISender sender, CancellationToken ct) =>
            Handle(async () =>
            {
                var dto = await sender.Send(new UpdatePlannedTransactionCommand(
                    id, request.OperationTypeId, RequireAmount(request.Amount), request.CurrencyId, request.WalletId,
                    request.Description, request.StartDate, ToInput(request.Recurrence)), ct);
                return Results.Ok(ToResponse(dto));
            })).WithName("updatePlannedTransaction");

        group.MapPost("/{id:guid}/finish", (Guid id, ISender sender, CancellationToken ct) => Handle(async () =>
            Results.Ok(ToResponse(await sender.Send(new FinishPlannedTransactionCommand(id), ct)))))
            .WithName("finishPlannedTransaction");

        group.MapDelete("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Handle(async () =>
        {
            var result = await sender.Send(new DeletePlannedTransactionCommand(id), ct);
            return result.FinishedPlan is { } finished ? Results.Ok(ToResponse(finished)) : Results.NoContent();
        })).WithName("deletePlannedTransaction");
    }

    private static void MapOccurrences(IEndpointRouteBuilder app)
    {
        app.MapGet("/planned-occurrences", (
            ISender sender, CancellationToken ct,
            [FromQuery] DateOnly? from = null, [FromQuery] DateOnly? to = null, [FromQuery] Guid? plannedTransactionId = null) =>
            Handle(async () =>
            {
                if (from is not { } fromDate || to is not { } toDate)
                {
                    throw new PlannedOccurrenceRangeInvalidException("Укажите период: from и to.");
                }

                var list = await sender.Send(new ListPlannedOccurrencesQuery(fromDate, toDate, plannedTransactionId), ct);
                return Results.Ok(new PlannedOccurrenceListResponse(list.From, list.To, list.Data.Select(ToResponse).ToList()));
            })).WithTags("Planning").WithName("listPlannedOccurrences");

        var group = app.MapGroup("/planned-transactions/{id:guid}/occurrences/{date}").WithTags("Planning");

        group.MapPost("/skip", (Guid id, DateOnly date, ISender sender, CancellationToken ct) => Handle(async () =>
            Results.Ok(ToResponse(await sender.Send(new SkipPlannedOccurrenceCommand(id, date), ct)))))
            .WithName("skipPlannedOccurrence");

        group.MapPost("/unskip", (Guid id, DateOnly date, ISender sender, CancellationToken ct) => Handle(async () =>
            Results.Ok(ToResponse(await sender.Send(new UnskipPlannedOccurrenceCommand(id, date), ct)))))
            .WithName("unskipPlannedOccurrence");

        group.MapPost("/move", (Guid id, DateOnly date, [FromBody] PlannedOccurrenceMoveRequest request, ISender sender, CancellationToken ct) =>
            Handle(async () =>
            {
                var newDate = request.Date ?? throw new PlanningRequestInvalidException("Укажите новую дату наступления.");
                return Results.Ok(ToResponse(await sender.Send(new MovePlannedOccurrenceCommand(id, date, newDate), ct)));
            })).WithName("movePlannedOccurrence");

        group.MapPut("/amount", (Guid id, DateOnly date, [FromBody] PlannedOccurrenceAmountRequest request, ISender sender, CancellationToken ct) =>
            Handle(async () =>
                Results.Ok(ToResponse(await sender.Send(new ChangePlannedOccurrenceAmountCommand(id, date, request.Amount), ct)))))
            .WithName("changePlannedOccurrenceAmount");

        group.MapPost("/execute", (
            Guid id, DateOnly date,
            [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] PlannedOccurrenceExecuteRequest? request,
            ISender sender, CancellationToken ct) => Handle(async () =>
        {
            var result = await sender.Send(
                new ExecutePlannedOccurrenceCommand(id, date, request?.WalletId, request?.Amount, request?.OperationDate), ct);
            return Results.Created(
                $"/api/v1/operations/{result.OperationId}",
                new PlannedOccurrenceExecutionResultResponse(ToResponse(result.Occurrence), result.OperationId));
        })).WithName("executePlannedOccurrence");
    }

    private static async Task<IResult> Handle(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (PlanningNotFoundException ex)
        {
            return Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Not found", detail: ex.Message);
        }
        catch (PlanningConflictException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict, title: "Planning conflict", detail: ex.Message, type: ConflictErrorType);
        }
        catch (PlanningDomainException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest, title: "Planning validation failed", detail: ex.Message, type: ValidationErrorType);
        }
    }

    private static decimal RequireAmount(decimal? amount) => amount ?? throw new PlanningRequestInvalidException("Укажите сумму.");

    private static RecurrenceInput? ToInput(RecurrenceContract? contract) => contract is null
        ? null
        : new RecurrenceInput(ParseName<RecurrenceFrequency>(contract.Frequency, "frequency"), contract.Interval, contract.Until, contract.Count);

    /// <summary>Only member names, case-insensitive: numbers such as "1" are rejected.</summary>
    private static T ParseName<T>(string? value, string field) where T : struct, Enum =>
        !string.IsNullOrWhiteSpace(value) && !char.IsDigit(value.Trim()[0]) && !value.Trim().StartsWith('-')
            && Enum.TryParse<T>(value.Trim(), ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : throw new PlanningRequestInvalidException($"Недопустимое значение {field}: '{value}'.");

    private static MoneyResponse Money(decimal amount, Guid currencyId) => new(amount.ToString(CultureInfo.InvariantCulture), currencyId);

    private static PlannedTransactionResponse ToResponse(PlannedTransactionDto dto) => new(
        dto.Id, dto.OperationTypeId, dto.Direction.ToString(), Money(dto.Amount, dto.CurrencyId), dto.WalletId,
        dto.Description, dto.StartDate,
        dto.Recurrence is { } r ? new RecurrenceContract(r.Frequency.ToString(), r.Interval, r.Until, r.Count) : null,
        dto.Status.ToString(), dto.HasExecutions, dto.CreatedAt, dto.UpdatedAt, dto.FinishedAt);

    private static PlannedOccurrenceResponse ToResponse(PlannedOccurrenceDto dto) => new(
        dto.PlannedTransactionId, dto.OccurrenceDate, dto.EffectiveDate, dto.OperationTypeId, dto.Direction.ToString(),
        dto.WalletId, dto.Description,
        Money(dto.PlannedAmount, dto.CurrencyId), Money(dto.ExecutedAmount, dto.CurrencyId), Money(dto.ExpectedAmount, dto.CurrencyId),
        dto.Status.ToString(), dto.IsOverdue, dto.IsMoved, dto.IsAmountChanged,
        dto.Executions.Select(e => new PlannedOccurrenceExecutionResponse(
            e.OperationId, Money(e.Amount, dto.CurrencyId), e.IsOperationDeleted, e.ExecutedAt)).ToList());
}
