using System.Globalization;
using LupexWallet.Budgeting.Application;
using LupexWallet.Budgeting.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Budgeting.Api;

/// <summary>
/// HTTP endpoints of the Budgeting module (docs/api/openapi.yaml, tags ExpenseCategories and Budgets;
/// ADR-0010 §5). Mapped by Host inside the authorized /api/v1 group.
/// </summary>
public static class BudgetingEndpointsExtensions
{
    public static IEndpointRouteBuilder MapBudgetingEndpoints(this IEndpointRouteBuilder app)
    {
        MapExpenseCategories(app);
        MapBudgets(app);
        return app;
    }

    private static void MapExpenseCategories(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/expense-categories").WithTags("ExpenseCategories");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] bool includeInactive = false, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListExpenseCategoriesQuery(includeInactive, cursor, limit), ct);
            return Results.Ok(new ExpenseCategoryPageResponse(
                page.Data.Select(ToResponse).ToList(), new BudgetingPageMeta(page.NextCursor, page.HasMore)));
        }).WithName("listExpenseCategories");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new GetExpenseCategoryQuery(id), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        }).WithName("getExpenseCategory");

        group.MapPost("/", (ExpenseCategoryCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            var dto = await sender.Send(new CreateExpenseCategoryCommand(request.Name, request.OperationTypeIds ?? []), ct);
            return Results.Created($"/api/v1/expense-categories/{dto.Id}", ToResponse(dto));
        })).WithName("createExpenseCategory");

        group.MapPatch("/{id:guid}", (Guid id, ExpenseCategoryUpdateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            var dto = await sender.Send(new UpdateExpenseCategoryCommand(id, request.Name, request.OperationTypeIds), ct);
            return Results.Ok(ToResponse(dto));
        })).WithName("updateExpenseCategory");

        group.MapPost("/{id:guid}/deactivate", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
            Results.Ok(ToResponse(await sender.Send(new DeactivateExpenseCategoryCommand(id), ct)))))
            .WithName("deactivateExpenseCategory");

        group.MapDelete("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            await sender.Send(new DeleteExpenseCategoryCommand(id), ct);
            return Results.NoContent();
        })).WithName("deleteExpenseCategory");
    }

    private static void MapBudgets(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/budgets").WithTags("Budgets");

        group.MapGet("/", (
            ISender sender, CancellationToken ct,
            [FromQuery] string? month = null, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) => Execute(async () =>
        {
            var page = await sender.Send(new ListBudgetsQuery(month, cursor, limit), ct);
            return Results.Ok(new BudgetPageResponse(page.Data.Select(ToResponse).ToList(), new BudgetingPageMeta(page.NextCursor, page.HasMore)));
        })).WithName("listBudgets");

        group.MapPost("/", (BudgetCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            var dto = await sender.Send(new CreateBudgetCommand(request.Month, request.CurrencyId), ct);
            return Results.Created($"/api/v1/budgets/{dto.Id}", ToResponse(dto));
        })).WithName("createBudget");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new GetBudgetQuery(id), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        }).WithName("getBudget");

        group.MapDelete("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            await sender.Send(new DeleteBudgetCommand(id), ct);
            return Results.NoContent();
        })).WithName("deleteBudget");

        group.MapPut("/{id:guid}/limits/{categoryId:guid}", (Guid id, Guid categoryId, BudgetLimitRequest request, ISender sender, CancellationToken ct) =>
            Execute(async () =>
            {
                if (request.Limit is not { } limit)
                {
                    throw new BudgetLimitInvalidException();
                }

                return Results.Ok(ToResponse(await sender.Send(new SetBudgetCategoryLimitCommand(id, categoryId, limit), ct)));
            })).WithName("setBudgetCategoryLimit");

        group.MapDelete("/{id:guid}/limits/{categoryId:guid}", (Guid id, Guid categoryId, ISender sender, CancellationToken ct) =>
            Execute(async () => Results.Ok(ToResponse(await sender.Send(new RemoveBudgetCategoryLimitCommand(id, categoryId), ct)))))
            .WithName("removeBudgetCategoryLimit");

        group.MapGet("/{id:guid}/execution", async (Guid id, ISender sender, CancellationToken ct) =>
        {
            var dto = await sender.Send(new GetBudgetExecutionQuery(id), ct);
            return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
        }).WithName("getBudgetExecution");
    }

    private static async Task<IResult> Execute(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (BudgetingValidationException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Budgeting validation failed",
                detail: ex.Message,
                type: "https://lupexwallet/errors/budgeting-validation-error");
        }
        catch (BudgetingConflictException ex)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Budgeting conflict",
                detail: ex.Message,
                type: "https://lupexwallet/errors/budgeting-conflict");
        }
    }

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static ExpenseCategoryResponse ToResponse(ExpenseCategoryDto dto) => new(dto.Id, dto.Name, dto.IsActive, dto.OperationTypeIds);

    private static BudgetResponse ToResponse(BudgetDto dto) => new(
        dto.Id, dto.Month, dto.CurrencyId,
        dto.Limits.Select(l => new CategoryLimitResponse(l.ExpenseCategoryId, Format(l.Limit))).ToList(),
        dto.CreatedAt, dto.UpdatedAt);

    private static BudgetExecutionResponse ToResponse(BudgetExecutionDto dto) => new(
        dto.BudgetId,
        dto.Month,
        dto.CurrencyId,
        dto.Categories.Select(c => new CategoryExecutionResponse(
            c.ExpenseCategoryId, c.Name, c.IsActive, Format(c.Limit), Format(c.Actual), Format(c.Remaining), c.IsOverspent)).ToList(),
        new UnbudgetedExecutionResponse(Format(dto.UnbudgetedActual)),
        new BudgetTotalsResponse(Format(dto.TotalLimit), Format(dto.TotalBudgetedActual), Format(dto.TotalRemaining), Format(dto.TotalActual)),
        dto.RateSource,
        dto.AppliedRates.Select(r => new AppliedRateResponse(r.FromCurrencyId, r.OperationDate, r.RateDate, Format(r.Rate), r.IsFallback)).ToList(),
        dto.ExcludedOperations.Select(o => new ExcludedOperationResponse(o.OperationId, o.CurrencyId, o.OperationDate, Format(o.Amount), o.Reason)).ToList());
}
