using System.Globalization;
using LupexWallet.Debts.Application;
using LupexWallet.Debts.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Debts.Api;

/// <summary>HTTP endpoints of the Debts module (docs/api/openapi.yaml, tags Creditors, Debts, Loans).</summary>
public static class DebtsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapDebtsEndpoints(this IEndpointRouteBuilder app)
    {
        MapCreditors(app);
        MapDebts(app);
        MapLoans(app);
        return app;
    }

    private static void MapCreditors(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/creditors").WithTags("Creditors");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] bool includeInactive = false, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListCreditorsQuery(includeInactive, cursor, limit), ct);
            return Results.Ok(new CreditorPageResponse(page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore)));
        }).WithName("listCreditors");

        group.MapPost("/", ([FromBody] CreditorCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            var dto = await sender.Send(new CreateCreditorCommand(request.Name), ct);
            return Results.Created($"/api/v1/creditors/{dto.Id}", ToResponse(dto));
        })).WithName("createCreditor");

        group.MapPatch("/{id:guid}", (Guid id, [FromBody] CreditorUpdateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
            Results.Ok(ToResponse(await sender.Send(new RenameCreditorCommand(id, request.Name), ct)))))
            .WithName("renameCreditor");

        group.MapPost("/{id:guid}/deactivate", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
            Results.Ok(ToResponse(await sender.Send(new DeactivateCreditorCommand(id), ct)))))
            .WithName("deactivateCreditor");

        group.MapDelete("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            await sender.Send(new DeleteCreditorCommand(id), ct);
            return Results.NoContent();
        }, $"/api/v1/creditors/{id}")).WithName("deleteCreditor");
    }

    private static void MapDebts(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/debts").WithTags("Debts");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] Guid? creditorId = null, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListDebtsQuery(creditorId, cursor, limit), ct);
            return Results.Ok(new DebtPageResponse(page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore)));
        }).WithName("listDebts");

        group.MapGet("/summary", async (ISender sender, CancellationToken ct) =>
            Results.Ok(ToResponse(await sender.Send(new GetDebtSummaryQuery(), ct)))).WithName("getDebtSummary");

        group.MapPost("/", ([FromBody] DebtCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { CreditorId: { } creditorId, CurrencyId: { } currencyId, OriginalAmount: { } amount, StartDate: { } startDate })
            {
                return MissingFields("creditorId, name, currencyId, originalAmount, startDate");
            }

            var dto = await sender.Send(new CreateDebtCommand(creditorId, request.Name ?? string.Empty, currencyId, amount, startDate), ct);
            return Results.Created($"/api/v1/debts/{dto.Id}", ToResponse(dto));
        })).WithName("createDebt");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            await sender.Send(new GetDebtQuery(id), ct) is { } dto ? Results.Ok(ToResponse(dto)) : Results.NotFound())
            .WithName("getDebt");

        group.MapPatch("/{id:guid}", (Guid id, [FromBody] DebtUpdateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { CreditorId: { } creditorId, OriginalAmount: { } amount, StartDate: { } startDate })
            {
                return MissingFields("creditorId, name, originalAmount, startDate");
            }

            var dto = await sender.Send(new UpdateDebtCommand(id, creditorId, request.Name ?? string.Empty, amount, startDate), ct);
            return Results.Ok(ToResponse(dto));
        }, $"/api/v1/debts/{id}")).WithName("updateDebt");

        group.MapPost("/{id:guid}/cancel", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
            Results.Ok(ToResponse(await sender.Send(new CancelDebtCommand(id), ct))), $"/api/v1/debts/{id}"))
            .WithName("cancelDebt");

        group.MapDelete("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            await sender.Send(new DeleteDebtCommand(id), ct);
            return Results.NoContent();
        }, $"/api/v1/debts/{id}")).WithName("deleteDebt");

        group.MapPost("/{id:guid}/repayments", (Guid id, [FromBody] PaymentCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { WalletId: { } walletId, OperationTypeId: { } typeId, Amount: { } amount, PaymentDate: { } date })
            {
                return MissingFields("walletId, operationTypeId, amount, paymentDate");
            }

            var dto = await sender.Send(new RecordDebtRepaymentCommand(id, walletId, typeId, amount, date), ct);
            return Results.Created($"/api/v1/debts/{id}", ToResponse(dto));
        }, $"/api/v1/debts/{id}")).WithName("recordDebtRepayment");
    }

    private static void MapLoans(IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/loans").WithTags("Loans");

        group.MapGet("/", async (
            ISender sender, CancellationToken ct,
            [FromQuery] Guid? creditorId = null, [FromQuery] string? cursor = null, [FromQuery] int limit = 20) =>
        {
            var page = await sender.Send(new ListLoansQuery(creditorId, cursor, limit), ct);
            return Results.Ok(new LoanPageResponse(page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore)));
        }).WithName("listLoans");

        group.MapPost("/", ([FromBody] LoanCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { CreditorId: { } creditorId, CurrencyId: { } currencyId, AmountToRepay: { } amount, StartDate: { } startDate })
            {
                return MissingFields("creditorId, name, currencyId, amountToRepay, startDate");
            }

            var dto = await sender.Send(new CreateLoanCommand(creditorId, request.Name ?? string.Empty, currencyId, amount, startDate), ct);
            return Results.Created($"/api/v1/loans/{dto.Id}", ToResponse(dto));
        })).WithName("createLoan");

        group.MapGet("/{id:guid}", async (Guid id, ISender sender, CancellationToken ct) =>
            await sender.Send(new GetLoanQuery(id), ct) is { } dto ? Results.Ok(ToResponse(dto)) : Results.NotFound())
            .WithName("getLoan");

        group.MapPatch("/{id:guid}", (Guid id, [FromBody] LoanUpdateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { CreditorId: { } creditorId, AmountToRepay: { } amount, StartDate: { } startDate })
            {
                return MissingFields("creditorId, name, amountToRepay, startDate");
            }

            var dto = await sender.Send(new UpdateLoanCommand(id, creditorId, request.Name ?? string.Empty, amount, startDate), ct);
            return Results.Ok(ToResponse(dto));
        }, $"/api/v1/loans/{id}")).WithName("updateLoan");

        group.MapPut("/{id:guid}/schedule", (Guid id, [FromBody] LoanScheduleReplaceRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request.Items is null || request.Items.Any(x => x.DueDate is null || x.Amount is null))
            {
                return MissingFields("items[].dueDate, items[].amount");
            }

            var entries = request.Items.Select(x => new LoanScheduleEntry(x.DueDate!.Value, x.Amount!.Value)).ToList();
            return Results.Ok(ToResponse(await sender.Send(new ReplaceLoanScheduleCommand(id, entries), ct)));
        }, $"/api/v1/loans/{id}")).WithName("replaceLoanSchedule");

        group.MapPost("/{id:guid}/schedule/generate", (Guid id, [FromBody] LoanScheduleGenerateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { FirstPaymentDate: { } firstDate, NumberOfPayments: { } count })
            {
                return MissingFields("firstPaymentDate, numberOfPayments");
            }

            return Results.Ok(ToResponse(await sender.Send(new GenerateLoanScheduleCommand(id, firstDate, count), ct)));
        }, $"/api/v1/loans/{id}")).WithName("generateLoanSchedule");

        group.MapPost("/{id:guid}/cancel", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
            Results.Ok(ToResponse(await sender.Send(new CancelLoanCommand(id), ct))), $"/api/v1/loans/{id}"))
            .WithName("cancelLoan");

        group.MapDelete("/{id:guid}", (Guid id, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            await sender.Send(new DeleteLoanCommand(id), ct);
            return Results.NoContent();
        }, $"/api/v1/loans/{id}")).WithName("deleteLoan");

        group.MapPost("/{id:guid}/payments", (Guid id, [FromBody] PaymentCreateRequest request, ISender sender, CancellationToken ct) => Execute(async () =>
        {
            if (request is not { WalletId: { } walletId, OperationTypeId: { } typeId, Amount: { } amount, PaymentDate: { } date })
            {
                return MissingFields("walletId, operationTypeId, amount, paymentDate");
            }

            var dto = await sender.Send(new RecordLoanPaymentCommand(id, walletId, typeId, amount, date), ct);
            return Results.Created($"/api/v1/loans/{id}", ToResponse(dto));
        }, $"/api/v1/loans/{id}")).WithName("recordLoanPayment");
    }

    /// <summary>Common error mapping: 404 unknown id, 400 invalid input/reference, 409 state conflict.</summary>
    private static async Task<IResult> Execute(Func<Task<IResult>> action, string? instance = null)
    {
        try
        {
            return await action();
        }
        catch (KeyNotFoundException)
        {
            return Results.NotFound();
        }
        catch (DebtsDomainException ex) when (DebtsProblemDetails.TryCreate(ex, instance) is { } problem)
        {
            return Results.Problem(problem);
        }

        // Errors of other modules raised inside a Debts command (the payment operation, the linked reserve)
        // fall through to the Host mapping (docs/api/api-design.md, «Ошибки вложенных команд»).
    }

    private static IResult MissingFields(string fields) =>
        Results.Problem(DebtsProblemDetails.Validation($"Заполните обязательные поля: {fields}."));

    private static MoneyResponse Money(decimal amount, Guid currencyId) => new(amount.ToString(CultureInfo.InvariantCulture), currencyId);

    private static CreditorResponse ToResponse(CreditorDto dto) => new(dto.Id, dto.Name, dto.IsActive);

    private static PaymentResponse ToResponse(PaymentDto dto, Guid currencyId) => new(
        dto.Id, dto.OperationId, dto.WalletId, Money(dto.Amount, currencyId), dto.PaymentDate,
        dto.IsCounted, dto.IsOperationDeleted, dto.CreatedAt);

    private static DebtResponse ToResponse(DebtDto dto) => new(
        dto.Id, dto.CreditorId, dto.Name, dto.CurrencyId,
        Money(dto.OriginalAmount, dto.CurrencyId), Money(dto.RepaidAmount, dto.CurrencyId), Money(dto.RemainingAmount, dto.CurrencyId),
        dto.StartDate, dto.Status, dto.CancelledAt, dto.CreatedAt, dto.UpdatedAt,
        dto.Repayments.Select(x => ToResponse(x, dto.CurrencyId)).ToList());

    private static DebtSummaryResponse ToResponse(DebtSummaryDto dto) => new(
        dto.Currencies.Select(c => new DebtCurrencySummaryResponse(
            c.CurrencyId, Money(c.TotalRemainingAmount, c.CurrencyId), c.DebtCount,
            c.Creditors.Select(x => new DebtCreditorSummaryResponse(x.CreditorId, x.CreditorName, Money(x.RemainingAmount, c.CurrencyId), x.DebtCount)).ToList()))
        .ToList());

    private static LoanScheduleItemResponse ToResponse(LoanScheduleItemDto dto, Guid currencyId) => new(
        dto.DueDate, Money(dto.Amount, currencyId), Money(dto.PaidAmount, currencyId), Money(dto.OutstandingAmount, currencyId), dto.Status);

    private static LoanResponse ToResponse(LoanDto dto) => new(
        dto.Id, dto.CreditorId, dto.Name, dto.CurrencyId,
        Money(dto.AmountToRepay, dto.CurrencyId), Money(dto.PaidAmount, dto.CurrencyId),
        Money(dto.RemainingAmount, dto.CurrencyId), Money(dto.ScheduledAmount, dto.CurrencyId),
        dto.StartDate, dto.Status, dto.NextPayment is { } next ? ToResponse(next, dto.CurrencyId) : null,
        dto.Schedule.Select(x => ToResponse(x, dto.CurrencyId)).ToList(),
        dto.Payments.Select(x => ToResponse(x, dto.CurrencyId)).ToList(),
        dto.CancelledAt, dto.CreatedAt, dto.UpdatedAt);
}
