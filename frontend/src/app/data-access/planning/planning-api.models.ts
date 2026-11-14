// Mirrors docs/api/openapi.yaml (tag Planning — ADR-0010 §6): PlannedTransaction, Recurrence, PlannedOccurrence
// and the request schemas. Occurrences are computed by the server from the recurrence rule, not stored.

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export type RecurrenceFrequency = 'Daily' | 'Weekly' | 'Monthly' | 'Yearly';

/**
 * The day of week/month/year is taken from `startDate`; for Monthly a missing day of month falls back to the
 * last day of that month. At most one of `until`/`count` ends the rule; neither — it never ends.
 */
export interface Recurrence {
  frequency: RecurrenceFrequency;
  /** 1…1000, default 1. */
  interval?: number;
  /** The last possible occurrence date (inclusive). */
  until?: string | null;
  /** Total occurrences, the first one included. */
  count?: number | null;
}

export type PlanDirection = 'Income' | 'Expense';
export type PlanStatus = 'Active' | 'Finished';

export interface PlannedTransaction {
  id: string;
  operationTypeId: string;
  /** Taken from the operation type. */
  direction: PlanDirection;
  /** Carries the plan currency (`amount.currencyId`). */
  amount: Money;
  /** Reset to null if the wallet was deleted or changed its currency. */
  walletId?: string | null;
  description?: string | null;
  startDate: string;
  recurrence?: Recurrence | null;
  status: PlanStatus;
  hasExecutions: boolean;
  createdAt: string;
  updatedAt: string;
  finishedAt?: string | null;
}

export type PlannedTransactionPage = CursorPage<PlannedTransaction>;

/** POST and the full-replace PATCH share the shape. */
export interface PlannedTransactionWriteRequest {
  operationTypeId: string;
  amount: string;
  currencyId: string;
  walletId?: string | null;
  description?: string | null;
  startDate: string;
  /** null — a one-off plan. */
  recurrence?: Recurrence | null;
}

export type OccurrenceStatus = 'Expected' | 'PartiallyExecuted' | 'Executed' | 'Skipped';

export interface PlannedOccurrenceExecution {
  operationId: string;
  /** The current amount of the operation, 0 for a soft-deleted one. */
  amount: Money;
  isOperationDeleted: boolean;
  executedAt: string;
}

export interface PlannedOccurrence {
  plannedTransactionId: string;
  /** The original schedule date — the occurrence identifier in paths. */
  occurrenceDate: string;
  /** The actual date (after a move). */
  effectiveDate: string;
  operationTypeId: string;
  direction: PlanDirection;
  walletId?: string | null;
  description?: string | null;
  /** The plan amount or the changed occurrence amount. */
  plannedAmount: Money;
  executedAmount: Money;
  /** plannedAmount − executedAmount; 0 for skipped, executed and finished-plan occurrences. */
  expectedAmount: Money;
  status: OccurrenceStatus;
  /** effectiveDate is before today (UTC) and the occurrence is still expected. */
  isOverdue: boolean;
  isMoved: boolean;
  isAmountChanged: boolean;
  executions: PlannedOccurrenceExecution[];
}

/** Not paginated; the period is at most 731 days. */
export interface PlannedOccurrenceList {
  from: string;
  to: string;
  data: PlannedOccurrence[];
}

/** Every field is optional: plan wallet, remaining expected amount and effective date by default. */
export interface PlannedOccurrenceExecuteRequest {
  walletId?: string | null;
  amount?: string | null;
  operationDate?: string | null;
}

export interface PlannedOccurrenceExecutionResult {
  occurrence: PlannedOccurrence;
  operationId: string;
}
