using System.Globalization;
using LupexWallet.Funds.Application;
using LupexWallet.Funds.Domain;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Funds.Api;

/// <summary>
/// HTTP endpoints of the Funds module (docs/api/openapi.yaml, tag Funds; ADR-0010 §4) — mapped
/// from Host/Program.cs inside the /api/v1 group protected by RequireAuthorization().
/// </summary>
public static class FundsEndpointsExtensions
{
    public static IEndpointRouteBuilder MapFundsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/funds").WithTags("Funds");

        group.MapGet("/", async (
                ISender sender,
                CancellationToken ct,
                [FromQuery] bool includeClosed = false,
                [FromQuery] string? cursor = null,
                [FromQuery] int limit = 20) =>
            {
                var page = await sender.Send(new ListFundsQuery(includeClosed, cursor, limit), ct);
                return Results.Ok(new FundPageResponse(
                    page.Data.Select(ToResponse).ToList(), new CursorPageMeta(page.NextCursor, page.HasMore)));
            })
            .WithName("listFunds");

        group.MapPost("/", ([FromBody] FundCreateRequest request, ISender sender, CancellationToken ct) =>
                ExecuteAsync(async () =>
                {
                    if (request.TargetAmount is not { } target)
                    {
                        return Validation("Укажите целевую сумму фонда.");
                    }

                    var dto = await sender.Send(
                        new CreateFundCommand(request.Name ?? string.Empty, target, request.CurrencyId, request.LinkedDebtId, request.LinkedLoanId),
                        ct);
                    var response = ToResponse(dto);
                    return Results.Created($"/api/v1/funds/{response.Id}", response);
                }))
            .WithName("createFund");

        group.MapGet("/wallet-reservations", async (ISender sender, CancellationToken ct) =>
            {
                var items = await sender.Send(new ListWalletReservationsQuery(), ct);
                return Results.Ok(items.Select(ToResponse).ToList());
            })
            .WithName("listWalletReservations");

        group.MapGet("/wallet-reservations/{walletId:guid}", async (Guid walletId, ISender sender, CancellationToken ct) =>
            {
                var dto = await sender.Send(new GetWalletReservationQuery(walletId), ct);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getWalletReservation");

        group.MapGet("/{fundId:guid}", async (Guid fundId, ISender sender, CancellationToken ct) =>
            {
                var dto = await sender.Send(new GetFundQuery(fundId), ct);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getFund");

        group.MapPatch("/{fundId:guid}", (Guid fundId, [FromBody] FundUpdateRequest request, ISender sender, CancellationToken ct) =>
                ExecuteAsync(async () =>
                {
                    if (request.TargetAmount is not { } target)
                    {
                        return Validation("Укажите целевую сумму фонда.");
                    }

                    var dto = await sender.Send(
                        new UpdateFundCommand(fundId, request.Name ?? string.Empty, target, request.CurrencyId, request.LinkedDebtId, request.LinkedLoanId),
                        ct);
                    return Results.Ok(ToResponse(dto));
                }))
            .WithName("updateFund");

        group.MapDelete("/{fundId:guid}", (Guid fundId, ISender sender, CancellationToken ct) =>
                ExecuteAsync(async () =>
                {
                    await sender.Send(new DeleteFundCommand(fundId), ct);
                    return Results.NoContent();
                }))
            .WithName("deleteFund");

        group.MapPost("/{fundId:guid}/close", (Guid fundId, ISender sender, CancellationToken ct) =>
                ExecuteAsync(async () => Results.Ok(ToResponse(await sender.Send(new CloseFundCommand(fundId), ct)))))
            .WithName("closeFund");

        group.MapPost("/{fundId:guid}/reserve", (Guid fundId, [FromBody] FundAllocationRequest request, ISender sender, CancellationToken ct) =>
                ExecuteAsync(async () => request.Amount is not { } amount
                    ? Validation("Укажите сумму.")
                    : Results.Ok(ToResponse(await sender.Send(new ReserveFundCommand(fundId, request.WalletId, amount), ct)))))
            .WithName("reserveFund");

        group.MapPost("/{fundId:guid}/release", (Guid fundId, [FromBody] FundAllocationRequest request, ISender sender, CancellationToken ct) =>
                ExecuteAsync(async () => request.Amount is not { } amount
                    ? Validation("Укажите сумму.")
                    : Results.Ok(ToResponse(await sender.Send(new ReleaseFundCommand(fundId, request.WalletId, amount), ct)))))
            .WithName("releaseFund");

        return app;
    }

    /// <summary>Single mapping of Funds domain errors to ProblemDetails.</summary>
    private static async Task<IResult> ExecuteAsync(Func<Task<IResult>> action)
    {
        try
        {
            return await action();
        }
        catch (FundNotFoundException)
        {
            return Results.NotFound();
        }
        catch (Exception ex) when (FundsProblemDetails.TryCreate(ex) is { } problem)
        {
            return Results.Problem(problem);
        }
    }

    private static IResult Validation(string detail) => Results.Problem(FundsProblemDetails.Validation(detail));

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static MoneyResponse Money(decimal amount, Guid currencyId) => new(Format(amount), currencyId);

    private static FundResponse ToResponse(FundDto dto) => new(
        dto.Id,
        dto.Name,
        dto.CurrencyId,
        Money(dto.TargetAmount, dto.CurrencyId),
        Money(dto.ReservedAmount, dto.CurrencyId),
        Format(dto.Progress),
        Money(dto.DeficitAmount, dto.CurrencyId),
        dto.LinkedDebtId,
        dto.LinkedLoanId,
        dto.Status.ToString(),
        dto.HasAllocationHistory,
        dto.Allocations
            .Select(a => new FundAllocationResponse(a.WalletId, Money(a.Amount, dto.CurrencyId), Money(a.DeficitAmount, dto.CurrencyId)))
            .ToList(),
        dto.CreatedAt,
        dto.UpdatedAt,
        dto.ClosedAt);

    private static WalletReservationResponse ToResponse(WalletReservationDto dto) => new(
        dto.WalletId,
        dto.CurrencyId,
        Money(dto.BalanceAmount, dto.CurrencyId),
        Money(dto.ReservedAmount, dto.CurrencyId),
        Money(dto.FreeAmount, dto.CurrencyId),
        Money(dto.DeficitAmount, dto.CurrencyId));
}
