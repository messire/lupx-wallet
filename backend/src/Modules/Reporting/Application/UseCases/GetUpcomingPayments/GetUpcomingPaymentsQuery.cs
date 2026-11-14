using LupexWallet.Debts.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Planning.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Reporting.Application;

/// <summary>Kinds of an upcoming payment / forecast movement.</summary>
public static class MovementKinds
{
    public const string PlannedIncome = "PlannedIncome";
    public const string PlannedExpense = "PlannedExpense";
    public const string LoanPayment = "LoanPayment";
}

/// <param name="Date">Effective date (planned: after a move; loan: due date). Overdue items keep their date.</param>
/// <param name="Amount">Remaining amount to pay, positive, in <paramref name="CurrencyId"/>.</param>
/// <param name="ConvertedAmount">Amount in the primary currency; null when the currency has no rate.</param>
public sealed record UpcomingPaymentDto(
    string Kind,
    DateOnly Date,
    bool IsOverdue,
    decimal Amount,
    Guid CurrencyId,
    decimal? ConvertedAmount,
    Guid? PlannedTransactionId,
    DateOnly? OccurrenceDate,
    Guid? OperationTypeId,
    Guid? WalletId,
    Guid? LoanId,
    string? LoanName,
    Guid? CreditorId,
    string? CreditorName,
    string? ScheduleItemStatus);

/// <summary>
/// Upcoming payments (ADR-0010 §7, #12): expected planned expenses and unpaid loan schedule items
/// from today to today + days, overdue ones included. Shown separately, never subtracted from free funds.
/// </summary>
public sealed record UpcomingPaymentsDto(
    DateOnly From,
    DateOnly To,
    Guid CurrencyId,
    decimal TotalAmount,
    decimal OverdueAmount,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateDto> Rates,
    IReadOnlyList<MissingRateDto> MissingRates,
    IReadOnlyList<UpcomingPaymentDto> Items);

/// <summary>GET /reporting/upcoming-payments?days=N (default 30). Null — no primary wallet yet.</summary>
public sealed record GetUpcomingPaymentsQuery(int Days = GetUpcomingPaymentsQuery.DefaultDays)
    : IRequest<UpcomingPaymentsDto?>, IQuery<UpcomingPaymentsDto?>
{
    public const int DefaultDays = 30;
    public const int MaxDays = 366;

    /// <summary>
    /// How far back overdue planned occurrences are looked for (the Planning port needs a lower bound).
    /// Loan schedule items have no lower bound.
    /// </summary>
    public const int OverdueLookbackDays = 366;
}

public sealed class GetUpcomingPaymentsQueryHandler(
    IWalletTotalsSource walletTotalsSource,
    IPlannedOccurrencesSource plannedOccurrencesSource,
    IDebtObligationsLookup debtObligationsLookup,
    IExchangeRateLookup exchangeRateLookup,
    ICurrencyLookup currencyLookup) : IRequestHandler<GetUpcomingPaymentsQuery, UpcomingPaymentsDto?>
{
    public async Task<UpcomingPaymentsDto?> Handle(GetUpcomingPaymentsQuery request, CancellationToken cancellationToken)
    {
        var primary = (await walletTotalsSource.GetAllAsync(cancellationToken)).FirstOrDefault(w => w.IsPrimary);
        if (primary is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = today.AddDays(request.Days);
        var converter = new LatestRateConverter(exchangeRateLookup, currencyLookup, primary.CurrencyId);

        var planned = await plannedOccurrencesSource.ListExpectedAsync(
            today.AddDays(-GetUpcomingPaymentsQuery.OverdueLookbackDays), to, cancellationToken);
        var schedule = await debtObligationsLookup.ListUnpaidScheduleItemsAsync(to, cancellationToken);

        var items = new List<UpcomingPaymentDto>();
        foreach (var occurrence in planned.Where(o => o.SignedAmount < 0m))
        {
            var amount = -occurrence.SignedAmount;
            items.Add(new UpcomingPaymentDto(
                MovementKinds.PlannedExpense, occurrence.EffectiveDate, occurrence.IsOverdue, amount, occurrence.CurrencyId.Value,
                await converter.ConvertAsync(amount, occurrence.CurrencyId, cancellationToken),
                occurrence.PlannedTransactionId.Value, occurrence.OccurrenceDate, occurrence.OperationTypeId.Value,
                occurrence.WalletId?.Value, null, null, null, null, null));
        }

        foreach (var item in schedule)
        {
            items.Add(new UpcomingPaymentDto(
                MovementKinds.LoanPayment, item.DueDate, item.IsOverdue, item.OutstandingAmount, item.CurrencyId.Value,
                await converter.ConvertAsync(item.OutstandingAmount, item.CurrencyId, cancellationToken),
                null, null, null, null, item.LoanId, item.LoanName, item.CreditorId, item.CreditorName, item.Status));
        }

        var ordered = items.OrderBy(i => i.Date).ThenBy(i => i.Kind).ToList();
        return new UpcomingPaymentsDto(
            today,
            to,
            primary.CurrencyId.Value,
            ordered.Sum(i => i.ConvertedAmount ?? 0m),
            ordered.Where(i => i.IsOverdue).Sum(i => i.ConvertedAmount ?? 0m),
            converter.RatesAsOfDate(today),
            LatestRateConverter.Source,
            converter.AppliedRates,
            await converter.MissingRatesAsync(cancellationToken),
            ordered);
    }
}
