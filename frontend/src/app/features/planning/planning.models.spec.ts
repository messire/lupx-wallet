import { PlannedOccurrence } from '../../data-access/planning/planning-api.models';
import { isExecutable, occurrenceDisplayStatus, recurrenceLabel } from './planning.models';

function occurrence(overrides: Partial<PlannedOccurrence>): PlannedOccurrence {
  return {
    plannedTransactionId: 'p-1',
    occurrenceDate: '2026-11-01',
    effectiveDate: '2026-11-01',
    operationTypeId: 't-1',
    direction: 'Expense',
    plannedAmount: { amount: '100', currencyId: 'c-1' },
    executedAmount: { amount: '0', currencyId: 'c-1' },
    expectedAmount: { amount: '100', currencyId: 'c-1' },
    status: 'Expected',
    isOverdue: false,
    isMoved: false,
    isAmountChanged: false,
    executions: [],
    ...overrides,
  };
}

describe('planning.models', () => {
  it('describes a recurrence rule in Russian', () => {
    expect(recurrenceLabel(null)).toBe('Разово');
    expect(recurrenceLabel({ frequency: 'Monthly', interval: 1 })).toBe('Каждый месяц');
    expect(recurrenceLabel({ frequency: 'Weekly', interval: 2, until: '2026-12-31' })).toBe('Каждые 2 нед., до 31.12.2026');
    expect(recurrenceLabel({ frequency: 'Daily', count: 10 })).toBe('Каждый день, 10 раз');
  });

  it('shows an overdue expected occurrence as «Просрочено»', () => {
    expect(occurrenceDisplayStatus(occurrence({ isOverdue: true }))).toBe('Overdue');
    expect(occurrenceDisplayStatus(occurrence({ status: 'Skipped' }))).toBe('Skipped');
  });

  it('allows executing only expected or partially executed occurrences', () => {
    expect(isExecutable(occurrence({ status: 'Expected' }))).toBe(true);
    expect(isExecutable(occurrence({ status: 'PartiallyExecuted' }))).toBe(true);
    expect(isExecutable(occurrence({ status: 'Executed' }))).toBe(false);
    expect(isExecutable(occurrence({ status: 'Skipped' }))).toBe(false);
  });
});
