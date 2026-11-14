import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';
import { ForecastDay, NegativePeriod } from '../../../../data-access/reporting/reporting-api.models';
import { formatAmount } from '@shared/money/money';
import { formatDateText } from '@shared/ui/date-input/calendar-date';
import { amountToPlotValue } from '../../reporting.models';

const WIDTH = 1000;
const HEIGHT = 240;
const PADDING_Y = 16;

export interface ForecastChartGeometry {
  linePath: string;
  zeroY: number;
  negativeAreas: { x: number; width: number }[];
  points: { x: number; y: number; negative: boolean; date: string }[];
  maxLabel: string;
  minLabel: string;
}

/** Whole days between two ISO dates (UTC, no time-zone drift). */
export function daysBetween(from: string, to: string): number {
  const [fy, fm, fd] = from.split('-').map(Number);
  const [ty, tm, td] = to.split('-').map(Number);
  return Math.round((Date.UTC(ty, tm - 1, td) - Date.UTC(fy, fm - 1, fd)) / 86_400_000);
}

/**
 * Step-chart geometry of the expected balance: the balance holds until the next day with movements, then jumps.
 * Exact amounts stay strings for every label (ADR-0005); numbers are only plot coordinates.
 */
export function buildForecastGeometry(days: ForecastDay[], from: string, to: string, negativePeriods: NegativePeriod[]): ForecastChartGeometry | null {
  if (days.length === 0) {
    return null;
  }
  const span = Math.max(1, daysBetween(from, to));
  const values = days.map((day) => amountToPlotValue(day.expectedBalance.amount));
  const max = Math.max(0, ...values);
  const min = Math.min(0, ...values);
  const range = max - min || 1;
  const x = (date: string) => Math.min(WIDTH, Math.max(0, (daysBetween(from, date) / span) * WIDTH));
  const y = (value: number) => PADDING_Y + ((max - value) / range) * (HEIGHT - 2 * PADDING_Y);

  const points = days.map((day, index) => ({ x: x(day.date), y: y(values[index]), negative: day.isNegative, date: day.date }));
  let linePath = `M ${points[0].x.toFixed(1)} ${points[0].y.toFixed(1)}`;
  for (const point of points.slice(1)) {
    linePath += ` H ${point.x.toFixed(1)} V ${point.y.toFixed(1)}`;
  }
  linePath += ` H ${WIDTH}`;

  const negativeAreas = negativePeriods.map((period) => {
    const start = x(period.from);
    // `to` is inclusive — the area covers the whole last day.
    const end = Math.min(WIDTH, x(period.to) + WIDTH / span);
    return { x: start, width: Math.max(2, end - start) };
  });

  const maxDay = days[values.indexOf(Math.max(...values))];
  const minDay = days[values.indexOf(Math.min(...values))];
  return {
    linePath,
    zeroY: y(0),
    negativeAreas,
    points,
    maxLabel: formatAmount(maxDay.expectedBalance.amount),
    minLabel: formatAmount(minDay.expectedBalance.amount),
  };
}

/** Expected balance over the forecast horizon as an SVG step chart (#10); the day table is its text alternative. */
@Component({
  selector: 'app-forecast-chart',
  standalone: true,
  templateUrl: './forecast-chart.component.html',
  styleUrl: './forecast-chart.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForecastChartComponent {
  readonly days = input.required<ForecastDay[]>();
  readonly from = input.required<string>();
  readonly to = input.required<string>();
  readonly negativePeriods = input<NegativePeriod[]>([]);
  readonly currencyCode = input('');

  protected readonly width = WIDTH;
  protected readonly height = HEIGHT;
  protected readonly formatDate = formatDateText;

  readonly geometry = computed(() => buildForecastGeometry(this.days(), this.from(), this.to(), this.negativePeriods()));

  readonly summary = computed(() => {
    const geometry = this.geometry();
    if (!geometry) {
      return 'Нет данных для графика';
    }
    const code = this.currencyCode();
    return `Ожидаемый остаток с ${formatDateText(this.from())} по ${formatDateText(this.to())}: максимум ${geometry.maxLabel} ${code}, минимум ${geometry.minLabel} ${code}.`;
  });
}
