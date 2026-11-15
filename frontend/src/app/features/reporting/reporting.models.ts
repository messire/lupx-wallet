import { AppliedRate, ForecastHorizon, ForecastMovementKind, UpcomingPaymentKind } from '../../data-access/reporting/reporting-api.models';
import { formatDateText } from '@shared/ui/date-input/calendar-date';

export const FORECAST_HORIZONS: { value: ForecastHorizon; label: string }[] = [
  { value: 1, label: '1 мес.' },
  { value: 3, label: '3 мес.' },
  { value: 6, label: '6 мес.' },
  { value: 12, label: '12 мес.' },
];

export const UPCOMING_DAY_OPTIONS = ['7', '30', '90'] as const;

export const UPCOMING_KIND_LABELS: Record<UpcomingPaymentKind, string> = {
  PlannedExpense: 'Плановый расход',
  LoanPayment: 'Платёж по кредиту',
};

export const MOVEMENT_KIND_LABELS: Record<ForecastMovementKind, string> = {
  PlannedIncome: 'Плановый доход',
  PlannedExpense: 'Плановый расход',
  LoanPayment: 'Платёж по кредиту',
};

/** True for a negative decimal string ("-0.01"); "-0" is not negative. */
export function isNegativeAmount(amount: string): boolean {
  const trimmed = amount.trim();
  return trimmed.startsWith('-') && !/^-0+(\.0+)?$/.test(trimmed);
}

/** "1 USD = 92.5 RUB · курс от 13.11.2026, Frankfurter". */
export function rateLabel(rate: AppliedRate, currencyCode: (id: string) => string): string {
  return `1 ${currencyCode(rate.fromCurrencyId)} = ${rate.rate} ${currencyCode(rate.toCurrencyId)} · курс от ${formatDateText(rate.rateDate)}, ${rate.source}`;
}

/**
 * Plot-only approximation of a decimal string (ADR-0005 forbids `number` for money arithmetic and display; a chart
 * coordinate is neither — the labels next to the chart always show the exact string).
 */
export function amountToPlotValue(amount: string): number {
  const value = Number.parseFloat(amount);
  return Number.isFinite(value) ? value : 0;
}
