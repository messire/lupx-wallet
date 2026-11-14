// Mirrors docs/api/openapi.yaml (tags Creditors, Debts, Loans — ADR-0010 §3): Creditor, Debt, DebtSummary,
// Loan, LoanScheduleItem, Payment and the request schemas.

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export interface Creditor {
  id: string;
  name: string;
  isActive: boolean;
}

export type CreditorPage = CursorPage<Creditor>;

/** `Repaid` is computed (counted payments cover the amount); `Cancelled` is terminal (ObligationStatus). */
export type ObligationStatus = 'Active' | 'Repaid' | 'Cancelled';

/**
 * A link between a debt/loan and a real expense operation (ADR-0010, В4). Amount, date and wallet are read from
 * the operation as it is now; a soft-deleted operation stops being counted (`isCounted = false`).
 */
export interface Payment {
  id: string;
  operationId: string;
  walletId: string;
  amount: Money;
  paymentDate: string;
  isCounted: boolean;
  isOperationDeleted: boolean;
  createdAt: string;
}

export interface Debt {
  id: string;
  creditorId: string;
  name: string;
  currencyId: string;
  originalAmount: Money;
  repaidAmount: Money;
  /** max(0, originalAmount − repaidAmount). */
  remainingAmount: Money;
  startDate: string;
  status: ObligationStatus;
  cancelledAt: string | null;
  createdAt: string;
  updatedAt: string;
  repayments: Payment[];
}

export type DebtPage = CursorPage<Debt>;

export interface DebtCreateRequest {
  creditorId: string;
  name: string;
  currencyId: string;
  originalAmount: string;
  startDate: string;
}

/** The currency of a debt never changes. */
export type DebtUpdateRequest = Omit<DebtCreateRequest, 'currencyId'>;

export interface DebtSummaryCreditor {
  creditorId: string;
  creditorName: string;
  remainingAmount: Money;
  debtCount: number;
}

/** Totals grouped by debt currency — no conversion into the primary wallet's currency here (ADR-0010 §7). */
export interface DebtSummaryCurrency {
  currencyId: string;
  totalRemainingAmount: Money;
  debtCount: number;
  creditors: DebtSummaryCreditor[];
}

export interface DebtSummary {
  currencies: DebtSummaryCurrency[];
}

export type LoanScheduleItemStatus = 'Paid' | 'Partial' | 'Overdue' | 'Expected';

export interface LoanScheduleItem {
  dueDate: string;
  amount: Money;
  paidAmount: Money;
  outstandingAmount: Money;
  status: LoanScheduleItemStatus;
}

export interface Loan {
  id: string;
  creditorId: string;
  name: string;
  currencyId: string;
  amountToRepay: Money;
  paidAmount: Money;
  remainingAmount: Money;
  /** Sum of the schedule items — may differ from amountToRepay. */
  scheduledAmount: Money;
  startDate: string;
  status: ObligationStatus;
  /** The earliest not fully paid item (overdue included); null for a cancelled loan or an empty/paid schedule. */
  nextPayment: LoanScheduleItem | null;
  schedule: LoanScheduleItem[];
  payments: Payment[];
  cancelledAt: string | null;
  createdAt: string;
  updatedAt: string;
}

export type LoanPage = CursorPage<Loan>;

export interface LoanCreateRequest {
  creditorId: string;
  name: string;
  currencyId: string;
  amountToRepay: string;
  startDate: string;
}

export type LoanUpdateRequest = Omit<LoanCreateRequest, 'currencyId'>;

export interface LoanScheduleItemInput {
  dueDate: string;
  amount: string;
}

export interface LoanScheduleReplaceRequest {
  items: LoanScheduleItemInput[];
}

export interface LoanScheduleGenerateRequest {
  firstPaymentDate: string;
  /** 1…600. */
  numberOfPayments: number;
}

/** Repayment of a debt / payment of a loan: a regular expense operation is created in the same transaction. */
export interface PaymentCreateRequest {
  /** A wallet in the debt/loan currency. */
  walletId: string;
  /** An active operation type with the Expense behavior. */
  operationTypeId: string;
  /** > 0 and not more than the remaining amount. */
  amount: string;
  paymentDate: string;
}
