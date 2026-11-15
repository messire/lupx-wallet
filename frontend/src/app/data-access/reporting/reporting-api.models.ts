// Mirrors docs/api/openapi.yaml (Reporting tag: TotalAmount, FreeFunds, UpcomingPayments, DebtsSummaryReport,
// CashFlowForecast and their nested schemas). Amounts are decimal strings (ADR-0005).

import { Money } from '../../shared/money/money';

/** A wallet excluded from the total because its currency has no exchange rate (ADR-0004 §9). */
export interface ExcludedWallet {
  walletId: string;
  reason: string;
}

/** The latest known rate applied by a report (rateDate — when the rate was fetched). */
export interface AppliedRate {
  fromCurrencyId: string;
  toCurrencyId: string;
  rate: string;
  rateDate: string;
  source: string;
}

/** Amounts in a currency without a rate — not included in the report totals. */
export interface MissingRate {
  currencyId: string;
  reason: string;
  itemCount: number;
}

export interface TotalAmount {
  /** Requested date; null means "current total" (UC-19). */
  date: string | null;
  /** Current total — free funds (= freeAmount, ADR-0010 В9); for a date — the actual amount. */
  amount: Money;
  actualAmount: Money;
  /** null for a historical total (reserve history is not stored). */
  reservedAmount: Money | null;
  freeAmount: Money | null;
  /** The rate date actually applied during conversion — may differ from the requested date. */
  ratesAsOfDate: string;
  excludedWallets: ExcludedWallet[];
}

export interface WalletFreeFunds {
  walletId: string;
  currencyId: string;
  includeInTotal: boolean;
  isPrimary: boolean;
  balance: Money;
  reserved: Money;
  /** balance − reserved, may be negative. */
  free: Money;
  deficit: Money;
}

export interface FreeFunds {
  actualAmount: Money;
  reservedAmount: Money;
  freeAmount: Money;
  deficitAmount: Money;
  ratesAsOfDate: string;
  rateSource: string;
  rates: AppliedRate[];
  excludedWallets: ExcludedWallet[];
  /** Every wallet (primary first), amounts in the wallet currency. */
  wallets: WalletFreeFunds[];
}

export type UpcomingPaymentKind = 'PlannedExpense' | 'LoanPayment';

export interface UpcomingPayment {
  kind: UpcomingPaymentKind;
  /** Occurrence date (after a move) or schedule item date. */
  date: string;
  isOverdue: boolean;
  /** Amount left to pay, in the item currency. */
  amount: Money;
  /** In the primary wallet currency; null without a rate. */
  convertedAmount: Money | null;
  plannedTransactionId?: string | null;
  /** Original occurrence date of the plan (before a move). */
  occurrenceDate?: string | null;
  operationTypeId?: string | null;
  walletId?: string | null;
  loanId?: string | null;
  loanName?: string | null;
  creditorId?: string | null;
  creditorName?: string | null;
  scheduleItemStatus?: 'Partial' | 'Overdue' | 'Expected' | null;
}

export interface UpcomingPayments {
  from: string;
  to: string;
  totalAmount: Money;
  overdueAmount: Money;
  ratesAsOfDate: string;
  rateSource: string;
  rates: AppliedRate[];
  missingRates: MissingRate[];
  items: UpcomingPayment[];
}

export interface CreditorDebt {
  creditorId: string;
  creditorName: string;
  amount: Money;
  debtCount: number;
}

export interface ObligationRemaining {
  id: string;
  name: string;
  creditorId: string;
  creditorName: string;
  /** In the debt/loan currency. */
  remainingAmount: Money;
  convertedAmount: Money | null;
}

export interface DebtsSummaryReport {
  totalDebtAmount: Money;
  debtCount: number;
  creditors: CreditorDebt[];
  debts: ObligationRemaining[];
  totalLoanRemainingAmount: Money;
  loanCount: number;
  loans: ObligationRemaining[];
  ratesAsOfDate: string;
  rateSource: string;
  rates: AppliedRate[];
  missingRates: MissingRate[];
}

export type ForecastMovementKind = 'PlannedIncome' | 'PlannedExpense' | 'LoanPayment';

export interface ForecastMovement {
  kind: ForecastMovementKind;
  /** Date in the forecast (an overdue item — today). */
  date: string;
  originalDate: string;
  isOverdue: boolean;
  /** Signed (income +, expense −), in the item currency. */
  amount: Money;
  /** Signed, in the calculation currency. */
  convertedAmount: Money;
  plannedTransactionId?: string | null;
  walletId?: string | null;
  loanId?: string | null;
  loanName?: string | null;
}

export interface ForecastDay {
  date: string;
  netChange: Money;
  /** Expected balance at the end of the day. */
  expectedBalance: Money;
  isNegative: boolean;
  movements: ForecastMovement[];
}

export interface NegativePeriod {
  from: string;
  /** Inclusive. */
  to: string;
}

export type ForecastHorizon = 1 | 3 | 6 | 12;

export interface CashFlowForecast {
  from: string;
  to: string;
  horizonMonths: ForecastHorizon;
  startBalance: Money;
  endBalance: Money;
  minBalance: Money;
  minBalanceDate: string;
  firstNegativeDate: string | null;
  negativePeriods: NegativePeriod[];
  /** Today and every date with movements, ascending. */
  days: ForecastDay[];
  ratesAsOfDate: string;
  rateSource: string;
  rates: AppliedRate[];
  /** Explicit calculation assumptions to show to the user. */
  assumptions: string[];
  excludedWallets: ExcludedWallet[];
  missingRates: MissingRate[];
}
