import { LoanScheduleItemStatus, ObligationStatus, Payment } from '../../data-access/debts/debts-api.models';
import { BadgeTone } from '@shared/ui/badge/badge.component';

/** Screen-state helpers of the Debts feature (debts and loans pages) — display labels, not API models. */

export const OBLIGATION_STATUS_LABELS: Record<ObligationStatus, string> = {
  Active: 'Активен',
  Repaid: 'Погашен',
  Cancelled: 'Отменён',
};

export const OBLIGATION_STATUS_TONES: Record<ObligationStatus, BadgeTone> = {
  Active: 'accent',
  Repaid: 'success',
  Cancelled: 'neutral',
};

export const SCHEDULE_STATUS_LABELS: Record<LoanScheduleItemStatus, string> = {
  Paid: 'Оплачен',
  Partial: 'Частично',
  Overdue: 'Просрочен',
  Expected: 'Ожидается',
};

export const SCHEDULE_STATUS_TONES: Record<LoanScheduleItemStatus, BadgeTone> = {
  Paid: 'success',
  Partial: 'warning',
  Overdue: 'danger',
  Expected: 'neutral',
};

/** A payment stops being counted when its operation was soft-deleted or no longer fits (Payment.isCounted). */
export function paymentStateLabel(payment: Payment): string {
  if (payment.isOperationDeleted) {
    return 'Операция удалена — не учитывается';
  }
  return payment.isCounted ? 'Учтён' : 'Не учитывается';
}

/** A wallet that can pay a debt/loan: same currency, not archived. */
export interface PaymentWalletOption {
  id: string;
  name: string;
  currencyId: string;
  accountingStartDate: string;
}

/** Values of the shared debt/loan create-edit form; `amount` is originalAmount (debt) or amountToRepay (loan). */
export interface ObligationFormValue {
  creditorId: string;
  name: string;
  currencyId: string;
  amount: string;
  startDate: string;
}

export function todayIso(): string {
  return new Date().toISOString().slice(0, 10);
}
