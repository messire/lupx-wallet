using FluentAssertions;
using LupexWallet.Debts.Application;
using LupexWallet.ExchangeRates.Application;
using LupexWallet.Funds.Application;
using LupexWallet.Planning.Application;
using LupexWallet.ReferenceData.Application;
using LupexWallet.Reporting.Application;
using LupexWallet.SharedKernel;
using LupexWallet.Wallets.Application;
using NSubstitute;
using Xunit;

namespace LupexWallet.UnitTests.Reporting;

/// <summary>
/// ADR-0010 §7 reports on mocked ports: cash-flow forecast (calculator and handler), free funds,
/// upcoming payments and the debts summary in the primary currency.
/// </summary>
public sealed class PlanningReportsTests
{
    private static readonly CurrencyId Eur = new(Guid.NewGuid());
    private static readonly CurrencyId Usd = new(Guid.NewGuid());
    private static readonly CurrencyId Xyz = new(Guid.NewGuid());
    private static readonly WalletId Main = WalletId.New();
    private static readonly WalletId UsdWallet = WalletId.New();
    private static readonly WalletId Hidden = WalletId.New();
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);
    private static readonly DateOnly RateDate = Today.AddDays(-2);

    private readonly IWalletTotalsSource _wallets = Substitute.For<IWalletTotalsSource>();
    private readonly IPlannedOccurrencesSource _planned = Substitute.For<IPlannedOccurrencesSource>();
    private readonly IDebtObligationsLookup _debts = Substitute.For<IDebtObligationsLookup>();
    private readonly IExchangeRateLookup _rates = Substitute.For<IExchangeRateLookup>();
    private readonly ICurrencyLookup _currencies = Substitute.For<ICurrencyLookup>();
    private readonly IFundReservationsLookup _reservations = Substitute.For<IFundReservationsLookup>();

    public PlanningReportsTests()
    {
        _wallets.GetAllAsync(Arg.Any<CancellationToken>()).Returns(new List<WalletTotalInfo>
        {
            new(Main, Eur, 1000m, IncludeInTotal: true, IsPrimary: true),
            new(UsdWallet, Usd, 100m, IncludeInTotal: true, IsPrimary: false),
            new(Hidden, Eur, 500m, IncludeInTotal: false, IsPrimary: false),
        });
        _rates.GetLatestRateWithDateAsync(Usd, Eur, Arg.Any<CancellationToken>())
            .Returns(((decimal Rate, DateOnly RatesAsOfDate)?)(0.5m, RateDate));
        _currencies.GetAsync(Xyz, Arg.Any<CancellationToken>()).Returns(new CurrencyLookupResult(Xyz, "XYZ"));
        _planned.ListExpectedAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _debts.ListUnpaidScheduleItemsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns([]);
        _debts.ListOpenObligationsAsync(Arg.Any<CancellationToken>()).Returns([]);
        _reservations.GetReservedByWalletAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private static ExpectedPlannedOccurrence Planned(DateOnly date, decimal signedAmount, CurrencyId? currency = null, WalletId? wallet = null) =>
        new(PlannedTransactionId.New(), date, date, OperationTypeId.New(), currency ?? Eur, wallet, signedAmount, IsOverdue: date < Today);

    private static UnpaidLoanScheduleItem LoanItem(DateOnly due, decimal outstanding, CurrencyId? currency = null) =>
        new(Guid.NewGuid(), "Кредит", Guid.NewGuid(), "Банк", currency ?? Eur, due, outstanding, 0m, outstanding,
            due < Today ? "Overdue" : "Expected", due < Today);

    // ---- CashFlowForecastCalculator ----

    private static ForecastMovementDto Move(DateOnly date, decimal converted) =>
        new(MovementKinds.PlannedExpense, date, date, false, converted, Guid.Empty, converted, null, null, null, null);

    [Fact]
    public void Calculator_AppliesMovementsInDateOrder_AndMarksNegativePeriods()
    {
        var to = Today.AddDays(60);
        var projection = CashFlowForecastCalculator.Project(Today, to, 100m,
        [
            Move(Today.AddDays(10), -150m),
            Move(Today.AddDays(20), 80m),
            Move(Today.AddDays(40), -50m),
            Move(Today.AddDays(5), 0m),
        ]);

        projection.Days.Select(d => d.Date).Should().Equal(Today, Today.AddDays(5), Today.AddDays(10), Today.AddDays(20), Today.AddDays(40));
        projection.Days.Select(d => d.ExpectedBalance).Should().Equal(100m, 100m, -50m, 30m, -20m);
        projection.FirstNegativeDate.Should().Be(Today.AddDays(10));
        projection.MinBalance.Should().Be(-50m);
        projection.MinBalanceDate.Should().Be(Today.AddDays(10));
        projection.NegativePeriods.Should().Equal(
            new NegativePeriodDto(Today.AddDays(10), Today.AddDays(19)),
            new NegativePeriodDto(Today.AddDays(40), to));
        projection.EndBalance.Should().Be(-20m);
    }

    [Fact]
    public void Calculator_NoMovements_OnlyTodayAndNoNegative()
    {
        var projection = CashFlowForecastCalculator.Project(Today, Today.AddMonths(1), 10m, []);

        projection.Days.Should().ContainSingle().Which.ExpectedBalance.Should().Be(10m);
        projection.FirstNegativeDate.Should().BeNull();
        projection.NegativePeriods.Should().BeEmpty();
    }

    [Fact]
    public void Calculator_MovementsOutsideTheHorizon_AreIgnored()
    {
        var projection = CashFlowForecastCalculator.Project(Today, Today.AddDays(10), 10m, [Move(Today.AddDays(11), -100m)]);

        projection.EndBalance.Should().Be(10m);
    }

    // ---- GetCashFlowForecastQuery ----

    private GetCashFlowForecastQueryHandler ForecastHandler() => new(_wallets, _planned, _debts, _rates, _currencies);

    [Fact]
    public async Task Forecast_StartIsIncludedWalletsConvertedByLatestRate()
    {
        var result = await ForecastHandler().Handle(new GetCashFlowForecastQuery(3), CancellationToken.None);

        result!.StartBalance.Should().Be(1000m + 100m * 0.5m, "the wallet outside the total is not counted");
        result.CurrencyId.Should().Be(Eur.Value);
        result.To.Should().Be(Today.AddMonths(3));
        result.RatesAsOfDate.Should().Be(RateDate);
        result.Rates.Should().ContainSingle(r => r.FromCurrencyId == Usd.Value && r.Rate == 0.5m && r.Source == "Frankfurter");
        result.Assumptions.Should().Contain(a => a.Contains("будущие изменения курсов не учитываются"));
    }

    [Fact]
    public async Task Forecast_OverdueItemsLandOnToday_AndPlannedOfHiddenWalletIsSkipped()
    {
        _planned.ListExpectedAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            Planned(Today.AddDays(-3), -100m),
            Planned(Today.AddDays(5), -999m, wallet: Hidden),
            Planned(Today.AddDays(7), 40m, currency: Usd),
        ]);
        _debts.ListUnpaidScheduleItemsAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
            [LoanItem(Today.AddDays(-10), 200m), LoanItem(Today.AddDays(20), 900m)]);

        var result = await ForecastHandler().Handle(new GetCashFlowForecastQuery(1), CancellationToken.None);

        var today = result!.Days.Single(d => d.Date == Today);
        today.Movements.Should().HaveCount(2).And.OnlyContain(m => m.IsOverdue && m.OriginalDate < Today);
        today.ExpectedBalance.Should().Be(1050m - 300m);
        result.Days.Should().NotContain(d => d.Date == Today.AddDays(5));
        result.Days.Single(d => d.Date == Today.AddDays(7)).Movements.Single().ConvertedAmount.Should().Be(20m);
        result.Days.Single(d => d.Date == Today.AddDays(20)).ExpectedBalance.Should().Be(750m + 20m - 900m);
        result.FirstNegativeDate.Should().Be(Today.AddDays(20));
        result.MinBalance.Should().Be(-130m);
    }

    [Fact]
    public async Task Forecast_MovementWithoutRate_IsLeftOutAndReported()
    {
        _planned.ListExpectedAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
            [Planned(Today.AddDays(2), -10m, currency: Xyz)]);

        var result = await ForecastHandler().Handle(new GetCashFlowForecastQuery(3), CancellationToken.None);

        result!.Days.Should().ContainSingle();
        result.MissingRates.Should().ContainSingle(m => m.CurrencyId == Xyz.Value && m.ItemCount == 1 && m.Reason == "Курс валюты XYZ недоступен.");
    }

    // ---- GetFreeFundsQuery ----

    [Fact]
    public async Task FreeFunds_PerWalletAndSummary_WithDeficit()
    {
        _reservations.GetReservedByWalletAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new WalletReservation(Main, Eur, 1200m),
            new WalletReservation(UsdWallet, Usd, 40m),
        ]);

        var result = await new GetFreeFundsQueryHandler(_wallets, _reservations, _rates, _currencies)
            .Handle(new GetFreeFundsQuery(), CancellationToken.None);

        var main = result!.Wallets.Single(w => w.WalletId == Main.Value);
        main.Free.Should().Be(-200m);
        main.Deficit.Should().Be(200m);
        result.Wallets.Single(w => w.WalletId == UsdWallet.Value).Free.Should().Be(60m);
        result.ActualAmount.Should().Be(1050m);
        result.ReservedAmount.Should().Be(1200m + 20m);
        result.FreeAmount.Should().Be(1050m - 1220m);
        result.DeficitAmount.Should().Be(200m);
        result.Wallets.Should().HaveCount(3, "all wallets are listed, the summary covers only the included ones");
    }

    // ---- GetUpcomingPaymentsQuery ----

    [Fact]
    public async Task Upcoming_ExpensesAndLoanItems_IncomesExcluded_OverdueIncluded()
    {
        _planned.ListExpectedAsync(Arg.Any<DateOnly>(), Arg.Any<DateOnly>(), Arg.Any<CancellationToken>()).Returns(
        [
            Planned(Today.AddDays(-1), -30m),
            Planned(Today.AddDays(3), 500m),
            Planned(Today.AddDays(4), -20m, currency: Usd),
        ]);
        _debts.ListUnpaidScheduleItemsAsync(Today.AddDays(30), Arg.Any<CancellationToken>()).Returns([LoanItem(Today.AddDays(10), 100m)]);

        var result = await new GetUpcomingPaymentsQueryHandler(_wallets, _planned, _debts, _rates, _currencies)
            .Handle(new GetUpcomingPaymentsQuery(), CancellationToken.None);

        result!.Items.Select(i => i.Kind).Should().Equal(MovementKinds.PlannedExpense, MovementKinds.PlannedExpense, MovementKinds.LoanPayment);
        result.Items[0].IsOverdue.Should().BeTrue();
        result.Items[1].ConvertedAmount.Should().Be(10m);
        result.TotalAmount.Should().Be(30m + 10m + 100m);
        result.OverdueAmount.Should().Be(30m);
        result.To.Should().Be(Today.AddDays(30));
        await _planned.Received(1).ListExpectedAsync(
            Today.AddDays(-GetUpcomingPaymentsQuery.OverdueLookbackDays), Today.AddDays(30), Arg.Any<CancellationToken>());
    }

    // ---- GetDebtsSummaryQuery ----

    [Fact]
    public async Task DebtsSummary_ConvertsToPrimary_GroupsByCreditor_LoansSeparately()
    {
        var bank = Guid.NewGuid();
        _debts.ListOpenObligationsAsync(Arg.Any<CancellationToken>()).Returns(
        [
            new OpenObligation(ObligationKind.Debt, Guid.NewGuid(), "A", bank, "Банк", Eur, 100m),
            new OpenObligation(ObligationKind.Debt, Guid.NewGuid(), "B", bank, "Банк", Usd, 50m),
            new OpenObligation(ObligationKind.Debt, Guid.NewGuid(), "C", Guid.NewGuid(), "Друг", Xyz, 7m),
            new OpenObligation(ObligationKind.Loan, Guid.NewGuid(), "Ипотека", bank, "Банк", Eur, 1000m),
        ]);

        var result = await new GetDebtsSummaryQueryHandler(_wallets, _debts, _rates, _currencies)
            .Handle(new GetDebtsSummaryQuery(), CancellationToken.None);

        result!.TotalDebtAmount.Should().Be(125m);
        result.DebtCount.Should().Be(3);
        result.Creditors.Should().ContainSingle().Which.Should().Be(new CreditorDebtDto(bank, "Банк", 125m, 2));
        result.TotalLoanRemainingAmount.Should().Be(1000m);
        result.Loans.Should().ContainSingle();
        result.MissingRates.Should().ContainSingle(m => m.CurrencyId == Xyz.Value);
        result.RatesAsOfDate.Should().Be(RateDate);
    }
}
