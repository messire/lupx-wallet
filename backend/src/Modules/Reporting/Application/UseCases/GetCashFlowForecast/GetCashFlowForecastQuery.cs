using LupexWallet.Debts.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Planning.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using MediatR;

namespace LupexWallet.Reporting.Application;

/// <summary>
/// CashFlowForecast read model (ADR-0010 §7, #10), computed on request and not stored. Calculation
/// currency = primary-wallet currency; latest known rates with their dates and source.
/// </summary>
public sealed record CashFlowForecastDto(
    Guid CurrencyId,
    DateOnly From,
    DateOnly To,
    int HorizonMonths,
    decimal StartBalance,
    decimal EndBalance,
    decimal MinBalance,
    DateOnly MinBalanceDate,
    DateOnly? FirstNegativeDate,
    IReadOnlyList<NegativePeriodDto> NegativePeriods,
    IReadOnlyList<ForecastDayDto> Days,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateDto> Rates,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<ExcludedWalletDto> ExcludedWallets,
    IReadOnlyList<MissingRateDto> MissingRates);

/// <summary>GET /reporting/cash-flow-forecast?months=1|3|6|12 (default 3). Null — no primary wallet yet.</summary>
public sealed record GetCashFlowForecastQuery(int Months = GetCashFlowForecastQuery.DefaultMonths)
    : IRequest<CashFlowForecastDto?>, IQuery<CashFlowForecastDto?>
{
    public const int DefaultMonths = 3;
    public static readonly IReadOnlyList<int> AllowedMonths = [1, 3, 6, 12];
}

public sealed class GetCashFlowForecastQueryHandler(
    IWalletTotalsSource walletTotalsSource,
    IPlannedOccurrencesSource plannedOccurrencesSource,
    IDebtObligationsLookup debtObligationsLookup,
    IExchangeRateLookup exchangeRateLookup,
    ICurrencyLookup currencyLookup) : IRequestHandler<GetCashFlowForecastQuery, CashFlowForecastDto?>
{
    public async Task<CashFlowForecastDto?> Handle(GetCashFlowForecastQuery request, CancellationToken cancellationToken)
    {
        var wallets = await walletTotalsSource.GetAllAsync(cancellationToken);
        var primary = wallets.FirstOrDefault(w => w.IsPrimary);
        if (primary is null)
        {
            return null;
        }

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var to = today.AddMonths(request.Months);
        var converter = new LatestRateConverter(exchangeRateLookup, currencyLookup, primary.CurrencyId);

        // Start: actual balances of the "include in total" wallets — reserves do not leave the wallet.
        var startBalance = 0m;
        var excludedWallets = new List<ExcludedWalletDto>();
        foreach (var wallet in wallets.Where(w => w.IncludeInTotal))
        {
            var rate = await converter.RateAsync(wallet.CurrencyId, cancellationToken);
            if (rate is null)
            {
                excludedWallets.Add(new ExcludedWalletDto(wallet.WalletId.Value, await converter.NoRateReasonAsync(wallet.CurrencyId, cancellationToken)));
                continue;
            }

            startBalance += wallet.CurrentBalance * rate.Value;
        }

        // A planned item bound to a wallet that is left out of the total (or excluded for a missing rate) does not move the total.
        var outsideTotal = wallets.Where(w => !w.IncludeInTotal).Select(w => w.WalletId)
            .Concat(excludedWallets.Select(w => new WalletId(w.WalletId)))
            .ToHashSet();

        var movements = new List<ForecastMovementDto>();
        var planned = await plannedOccurrencesSource.ListExpectedAsync(
            today.AddDays(-GetUpcomingPaymentsQuery.OverdueLookbackDays), to, cancellationToken);
        foreach (var o in planned)
        {
            if (o.WalletId is { } walletId && outsideTotal.Contains(walletId))
            {
                continue;
            }

            var converted = await converter.ConvertAsync(o.SignedAmount, o.CurrencyId, cancellationToken);
            if (converted is null)
            {
                continue;
            }

            movements.Add(new ForecastMovementDto(
                o.SignedAmount >= 0m ? MovementKinds.PlannedIncome : MovementKinds.PlannedExpense,
                Max(o.EffectiveDate, today), o.EffectiveDate, o.IsOverdue, o.SignedAmount, o.CurrencyId.Value, converted.Value,
                o.PlannedTransactionId.Value, o.WalletId?.Value, null, null));
        }

        foreach (var item in await debtObligationsLookup.ListUnpaidScheduleItemsAsync(to, cancellationToken))
        {
            var amount = -item.OutstandingAmount;
            var converted = await converter.ConvertAsync(amount, item.CurrencyId, cancellationToken);
            if (converted is null)
            {
                continue;
            }

            movements.Add(new ForecastMovementDto(
                MovementKinds.LoanPayment, Max(item.DueDate, today), item.DueDate, item.IsOverdue, amount, item.CurrencyId.Value,
                converted.Value, null, null, item.LoanId, item.LoanName));
        }

        var projection = CashFlowForecastCalculator.Project(today, to, startBalance, movements);
        var ratesAsOf = converter.RatesAsOfDate(today);

        return new CashFlowForecastDto(
            primary.CurrencyId.Value,
            today,
            to,
            request.Months,
            startBalance,
            projection.EndBalance,
            projection.MinBalance,
            projection.MinBalanceDate,
            projection.FirstNegativeDate,
            projection.NegativePeriods,
            projection.Days,
            ratesAsOf,
            LatestRateConverter.Source,
            converter.AppliedRates,
            Assumptions(ratesAsOf, converter.AppliedRates.Count > 0),
            excludedWallets,
            await converter.MissingRatesAsync(cancellationToken));
    }

    private static DateOnly Max(DateOnly a, DateOnly b) => a > b ? a : b;

    private static IReadOnlyList<string> Assumptions(DateOnly ratesAsOf, bool anyRates)
    {
        var list = new List<string>
        {
            "Начальный остаток — текущие фактические балансы кошельков с признаком «в общей сумме»; резервы фондов деньги из кошелька не выводят.",
            "Учитываются ожидаемые наступления плановых операций (с пропусками, переносами и частичным исполнением) и неоплаченные платежи по графикам кредитов.",
            $"Просроченные плановые операции (не старше {GetUpcomingPaymentsQuery.OverdueLookbackDays} дней) и платежи по кредитам учитываются на сегодня.",
            "Плановую операцию нельзя связать с кредитом, поэтому платёж по кредиту учитывается один раз — по графику.",
            "Плановые операции кошельков вне общей суммы не учитываются; плановые операции без кошелька учитываются.",
        };
        list.Add(anyRates
            ? $"Курсы валют — последние известные на {ratesAsOf:yyyy-MM-dd} (источник Frankfurter); будущие изменения курсов не учитываются."
            : "Все суммы в валюте основного кошелька — пересчёт курсов не потребовался.");
        return list;
    }
}
