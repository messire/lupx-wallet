import { OccurrenceStatus, PlannedOccurrence, Recurrence, RecurrenceFrequency } from '../../data-access/planning/planning-api.models';
import { BadgeTone } from '@shared/ui/badge/badge.component';
import { formatDateText } from '@shared/ui/date-input/calendar-date';

/** Screen-state helpers of the Planning feature — labels and derived display state, not API models. */

export const FREQUENCY_OPTIONS: ReadonlyArray<{ value: RecurrenceFrequency; label: string }> = [
  { value: 'Daily', label: 'Ежедневно' },
  { value: 'Weekly', label: 'Еженедельно' },
  { value: 'Monthly', label: 'Ежемесячно' },
  { value: 'Yearly', label: 'Ежегодно' },
];

const SINGLE: Record<RecurrenceFrequency, string> = {
  Daily: 'Каждый день',
  Weekly: 'Каждую неделю',
  Monthly: 'Каждый месяц',
  Yearly: 'Каждый год',
};

const UNIT: Record<RecurrenceFrequency, string> = {
  Daily: 'дн.',
  Weekly: 'нед.',
  Monthly: 'мес.',
  Yearly: 'г.',
};

/** "Разово", "Каждый месяц", "Каждые 2 нед., до 31.12.2026", "Каждый день, 10 раз". */
export function recurrenceLabel(recurrence: Recurrence | null | undefined): string {
  if (!recurrence) {
    return 'Разово';
  }
  const interval = recurrence.interval ?? 1;
  const base = interval === 1 ? SINGLE[recurrence.frequency] : `Каждые ${interval} ${UNIT[recurrence.frequency]}`;
  if (recurrence.until) {
    return `${base}, до ${formatDateText(recurrence.until)}`;
  }
  if (recurrence.count) {
    return `${base}, ${recurrence.count} раз`;
  }
  return base;
}

/** The status shown to the user: an overdue expected occurrence is «Просрочено», not «Ожидается». */
export type OccurrenceDisplayStatus = OccurrenceStatus | 'Overdue';

export function occurrenceDisplayStatus(occurrence: PlannedOccurrence): OccurrenceDisplayStatus {
  return occurrence.isOverdue ? 'Overdue' : occurrence.status;
}

export const OCCURRENCE_STATUS_LABELS: Record<OccurrenceDisplayStatus, string> = {
  Expected: 'Ожидается',
  PartiallyExecuted: 'Частично',
  Executed: 'Исполнено',
  Skipped: 'Пропущено',
  Overdue: 'Просрочено',
};

export const OCCURRENCE_STATUS_TONES: Record<OccurrenceDisplayStatus, BadgeTone> = {
  Expected: 'accent',
  PartiallyExecuted: 'warning',
  Executed: 'success',
  Skipped: 'neutral',
  Overdue: 'danger',
};

/** Can still be executed: expected, or partially executed with a remainder. */
export function isExecutable(occurrence: PlannedOccurrence): boolean {
  return occurrence.status === 'Expected' || occurrence.status === 'PartiallyExecuted';
}

/** A wallet offered by the plan and execute forms. */
export interface PlanWalletOption {
  id: string;
  name: string;
  currencyId: string;
  accountingStartDate: string;
}
