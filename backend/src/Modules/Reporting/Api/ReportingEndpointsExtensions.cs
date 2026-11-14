using System.Globalization;
using LupexWallet.Reporting.Application;
using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace LupexWallet.Reporting.Api;

/// <summary>
/// HTTP endpoints of the Reporting module (docs/api/openapi.yaml, Reporting tag) — called from
/// Host/Program.cs inside the /api/v1 group, already protected by RequireAuthorization().
/// UC-19/UC-21 — GET /total-amount (date optional); ADR-0010 §7 — free funds, upcoming payments,
/// debts summary, cash-flow forecast. Every report answers 404 while there is no primary wallet.
/// </summary>
public static class ReportingEndpointsExtensions
{
    private const string ValidationType = "https://lupexwallet/errors/reporting-validation-error";

    public static IEndpointRouteBuilder MapReportingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/reporting").WithTags("Reporting");

        group.MapGet("/total-amount", async (
                ISender sender,
                CancellationToken cancellationToken,
                [FromQuery] DateOnly? date = null) =>
            {
                var dto = await sender.Send(new GetTotalAmountQuery(date), cancellationToken);

                // Null — в системе еще нет ни одного кошелька, значит нет и основного
                // кошелька, задающего валюту отображения (см. GetTotalAmountQuery).
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getTotalAmount");

        group.MapGet("/free-funds", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var dto = await sender.Send(new GetFreeFundsQuery(), cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getFreeFunds");

        group.MapGet("/upcoming-payments", async (
                ISender sender,
                CancellationToken cancellationToken,
                [FromQuery] int? days = null) =>
            {
                var value = days ?? GetUpcomingPaymentsQuery.DefaultDays;
                if (value < 1 || value > GetUpcomingPaymentsQuery.MaxDays)
                {
                    return Validation($"Параметр days — целое число от 1 до {GetUpcomingPaymentsQuery.MaxDays}.");
                }

                var dto = await sender.Send(new GetUpcomingPaymentsQuery(value), cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getUpcomingPayments");

        group.MapGet("/debts-summary", async (ISender sender, CancellationToken cancellationToken) =>
            {
                var dto = await sender.Send(new GetDebtsSummaryQuery(), cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getReportingDebtsSummary");

        group.MapGet("/cash-flow-forecast", async (
                ISender sender,
                CancellationToken cancellationToken,
                [FromQuery] int? months = null) =>
            {
                var value = months ?? GetCashFlowForecastQuery.DefaultMonths;
                if (!GetCashFlowForecastQuery.AllowedMonths.Contains(value))
                {
                    return Validation("Параметр months — один из горизонтов 1, 3, 6 или 12 месяцев.");
                }

                var dto = await sender.Send(new GetCashFlowForecastQuery(value), cancellationToken);
                return dto is null ? Results.NotFound() : Results.Ok(ToResponse(dto));
            })
            .WithName("getCashFlowForecast");

        return app;
    }

    private static IResult Validation(string detail) => Results.Problem(
        statusCode: StatusCodes.Status400BadRequest, title: "Reporting validation failed", detail: detail, type: ValidationType);

    private static string Format(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static MoneyResponse Money(decimal amount, Guid currencyId) => new(Format(amount), currencyId);

    private static MoneyResponse? Money(decimal? amount, Guid currencyId) => amount is { } a ? Money(a, currencyId) : null;

    private static IReadOnlyList<AppliedRateResponse> Rates(IReadOnlyList<AppliedRateDto> rates) =>
        rates.Select(r => new AppliedRateResponse(r.FromCurrencyId, r.ToCurrencyId, Format(r.Rate), r.RateDate, r.Source)).ToList();

    private static IReadOnlyList<MissingRateResponse> Missing(IReadOnlyList<MissingRateDto> missing) =>
        missing.Select(m => new MissingRateResponse(m.CurrencyId, m.Reason, m.ItemCount)).ToList();

    private static IReadOnlyList<ExcludedWalletResponse> Excluded(IReadOnlyList<ExcludedWalletDto> wallets) =>
        wallets.Select(w => new ExcludedWalletResponse(w.WalletId, w.Reason)).ToList();

    private static TotalAmountResponse ToResponse(TotalAmountDto dto) => new(
        dto.Date,
        Money(dto.Amount, dto.CurrencyId),
        dto.RatesAsOfDate,
        Excluded(dto.ExcludedWallets),
        Money(dto.ActualAmount, dto.CurrencyId),
        Money(dto.ReservedAmount, dto.CurrencyId),
        Money(dto.FreeAmount, dto.CurrencyId));

    private static FreeFundsResponse ToResponse(FreeFundsDto dto) => new(
        Money(dto.ActualAmount, dto.CurrencyId),
        Money(dto.ReservedAmount, dto.CurrencyId),
        Money(dto.FreeAmount, dto.CurrencyId),
        Money(dto.DeficitAmount, dto.CurrencyId),
        dto.RatesAsOfDate,
        dto.RateSource,
        Rates(dto.Rates),
        Excluded(dto.ExcludedWallets),
        dto.Wallets.Select(w => new WalletFreeFundsResponse(
            w.WalletId, w.CurrencyId, w.IncludeInTotal, w.IsPrimary,
            Money(w.Balance, w.CurrencyId), Money(w.Reserved, w.CurrencyId), Money(w.Free, w.CurrencyId), Money(w.Deficit, w.CurrencyId)))
            .ToList());

    private static UpcomingPaymentsResponse ToResponse(UpcomingPaymentsDto dto) => new(
        dto.From,
        dto.To,
        Money(dto.TotalAmount, dto.CurrencyId),
        Money(dto.OverdueAmount, dto.CurrencyId),
        dto.RatesAsOfDate,
        dto.RateSource,
        Rates(dto.Rates),
        Missing(dto.MissingRates),
        dto.Items.Select(i => new UpcomingPaymentResponse(
            i.Kind, i.Date, i.IsOverdue, Money(i.Amount, i.CurrencyId), Money(i.ConvertedAmount, dto.CurrencyId),
            i.PlannedTransactionId, i.OccurrenceDate, i.OperationTypeId, i.WalletId, i.LoanId, i.LoanName,
            i.CreditorId, i.CreditorName, i.ScheduleItemStatus))
            .ToList());

    private static DebtsSummaryReportResponse ToResponse(DebtsSummaryReportDto dto)
    {
        ObligationRemainingResponse Obligation(ObligationRemainingDto o) => new(
            o.Id, o.Name, o.CreditorId, o.CreditorName, Money(o.RemainingAmount, o.CurrencyId), Money(o.ConvertedAmount, dto.CurrencyId));

        return new DebtsSummaryReportResponse(
            Money(dto.TotalDebtAmount, dto.CurrencyId),
            dto.DebtCount,
            dto.Creditors.Select(c => new CreditorDebtResponse(c.CreditorId, c.CreditorName, Money(c.Amount, dto.CurrencyId), c.DebtCount)).ToList(),
            dto.Debts.Select(Obligation).ToList(),
            Money(dto.TotalLoanRemainingAmount, dto.CurrencyId),
            dto.LoanCount,
            dto.Loans.Select(Obligation).ToList(),
            dto.RatesAsOfDate,
            dto.RateSource,
            Rates(dto.Rates),
            Missing(dto.MissingRates));
    }

    private static CashFlowForecastResponse ToResponse(CashFlowForecastDto dto) => new(
        dto.From,
        dto.To,
        dto.HorizonMonths,
        Money(dto.StartBalance, dto.CurrencyId),
        Money(dto.EndBalance, dto.CurrencyId),
        Money(dto.MinBalance, dto.CurrencyId),
        dto.MinBalanceDate,
        dto.FirstNegativeDate,
        dto.NegativePeriods.Select(p => new NegativePeriodResponse(p.From, p.To)).ToList(),
        dto.Days.Select(d => new ForecastDayResponse(
            d.Date,
            Money(d.NetChange, dto.CurrencyId),
            Money(d.ExpectedBalance, dto.CurrencyId),
            d.IsNegative,
            d.Movements.Select(m => new ForecastMovementResponse(
                m.Kind, m.Date, m.OriginalDate, m.IsOverdue, Money(m.Amount, m.CurrencyId), Money(m.ConvertedAmount, dto.CurrencyId),
                m.PlannedTransactionId, m.WalletId, m.LoanId, m.LoanName)).ToList()))
            .ToList(),
        dto.RatesAsOfDate,
        dto.RateSource,
        Rates(dto.Rates),
        dto.Assumptions,
        Excluded(dto.ExcludedWallets),
        Missing(dto.MissingRates));
}
