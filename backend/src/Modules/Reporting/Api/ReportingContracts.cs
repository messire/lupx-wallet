namespace LupexWallet.Reporting.Api;

// DTO по контракту docs/api/openapi.yaml (схема TotalAmount, дополнена полем excludedWallets —
// кошелёк без курса исключается из суммы с причиной, ADR-0004 §9; ADR-0010 §7 — actualAmount,
// reservedAmount, freeAmount и новые отчёты). Суммы — строки (ADR-0005).

public sealed record MoneyResponse(string Amount, Guid CurrencyId);

public sealed record ExcludedWalletResponse(Guid WalletId, string Reason);

public sealed record TotalAmountResponse(
    DateOnly? Date,
    MoneyResponse Amount,
    DateOnly RatesAsOfDate,
    IReadOnlyList<ExcludedWalletResponse> ExcludedWallets,
    MoneyResponse ActualAmount,
    MoneyResponse? ReservedAmount,
    MoneyResponse? FreeAmount);

public sealed record AppliedRateResponse(Guid FromCurrencyId, Guid ToCurrencyId, string Rate, DateOnly RateDate, string Source);

public sealed record MissingRateResponse(Guid CurrencyId, string Reason, int ItemCount);

public sealed record WalletFreeFundsResponse(
    Guid WalletId, Guid CurrencyId, bool IncludeInTotal, bool IsPrimary,
    MoneyResponse Balance, MoneyResponse Reserved, MoneyResponse Free, MoneyResponse Deficit);

public sealed record FreeFundsResponse(
    MoneyResponse ActualAmount,
    MoneyResponse ReservedAmount,
    MoneyResponse FreeAmount,
    MoneyResponse DeficitAmount,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateResponse> Rates,
    IReadOnlyList<ExcludedWalletResponse> ExcludedWallets,
    IReadOnlyList<WalletFreeFundsResponse> Wallets);

public sealed record UpcomingPaymentResponse(
    string Kind,
    DateOnly Date,
    bool IsOverdue,
    MoneyResponse Amount,
    MoneyResponse? ConvertedAmount,
    Guid? PlannedTransactionId,
    DateOnly? OccurrenceDate,
    Guid? OperationTypeId,
    Guid? WalletId,
    Guid? LoanId,
    string? LoanName,
    Guid? CreditorId,
    string? CreditorName,
    string? ScheduleItemStatus);

public sealed record UpcomingPaymentsResponse(
    DateOnly From,
    DateOnly To,
    MoneyResponse TotalAmount,
    MoneyResponse OverdueAmount,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateResponse> Rates,
    IReadOnlyList<MissingRateResponse> MissingRates,
    IReadOnlyList<UpcomingPaymentResponse> Items);

public sealed record CreditorDebtResponse(Guid CreditorId, string CreditorName, MoneyResponse Amount, int DebtCount);

public sealed record ObligationRemainingResponse(
    Guid Id, string Name, Guid CreditorId, string CreditorName, MoneyResponse RemainingAmount, MoneyResponse? ConvertedAmount);

public sealed record DebtsSummaryReportResponse(
    MoneyResponse TotalDebtAmount,
    int DebtCount,
    IReadOnlyList<CreditorDebtResponse> Creditors,
    IReadOnlyList<ObligationRemainingResponse> Debts,
    MoneyResponse TotalLoanRemainingAmount,
    int LoanCount,
    IReadOnlyList<ObligationRemainingResponse> Loans,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateResponse> Rates,
    IReadOnlyList<MissingRateResponse> MissingRates);

public sealed record ForecastMovementResponse(
    string Kind,
    DateOnly Date,
    DateOnly OriginalDate,
    bool IsOverdue,
    MoneyResponse Amount,
    MoneyResponse ConvertedAmount,
    Guid? PlannedTransactionId,
    Guid? WalletId,
    Guid? LoanId,
    string? LoanName);

public sealed record ForecastDayResponse(
    DateOnly Date, MoneyResponse NetChange, MoneyResponse ExpectedBalance, bool IsNegative, IReadOnlyList<ForecastMovementResponse> Movements);

public sealed record NegativePeriodResponse(DateOnly From, DateOnly To);

public sealed record CashFlowForecastResponse(
    DateOnly From,
    DateOnly To,
    int HorizonMonths,
    MoneyResponse StartBalance,
    MoneyResponse EndBalance,
    MoneyResponse MinBalance,
    DateOnly MinBalanceDate,
    DateOnly? FirstNegativeDate,
    IReadOnlyList<NegativePeriodResponse> NegativePeriods,
    IReadOnlyList<ForecastDayResponse> Days,
    DateOnly RatesAsOfDate,
    string RateSource,
    IReadOnlyList<AppliedRateResponse> Rates,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<ExcludedWalletResponse> ExcludedWallets,
    IReadOnlyList<MissingRateResponse> MissingRates);
