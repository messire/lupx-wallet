import { ForecastDay } from '../../../../data-access/reporting/reporting-api.models';
import { formatAmount } from '@shared/money/money';
import { buildForecastGeometry, daysBetween } from './forecast-chart.component';

function day(date: string, balance: string): ForecastDay {
  return {
    date,
    netChange: { amount: '0', currencyId: 'c' },
    expectedBalance: { amount: balance, currencyId: 'c' },
    isNegative: balance.startsWith('-'),
    movements: [],
  };
}

describe('forecast chart geometry', () => {
  it('counts whole days between ISO dates across months', () => {
    expect(daysBetween('2026-11-14', '2026-12-14')).toBe(30);
    expect(daysBetween('2026-11-14', '2026-11-14')).toBe(0);
  });

  it('returns null without days', () => {
    expect(buildForecastGeometry([], '2026-11-14', '2026-12-14', [])).toBeNull();
  });

  it('builds a step line, keeps exact string labels and marks negative periods', () => {
    const geometry = buildForecastGeometry(
      [day('2026-11-14', '1000.00'), day('2026-11-24', '-250.50'), day('2026-12-04', '400.00')],
      '2026-11-14',
      '2026-12-14',
      [{ from: '2026-11-24', to: '2026-12-03' }],
    )!;

    expect(geometry.linePath.startsWith('M 0.0')).toBe(true);
    expect(geometry.linePath).toContain(' H 333.3 V ');
    expect(geometry.linePath.endsWith(' H 1000')).toBe(true);
    expect(geometry.maxLabel).toBe(formatAmount('1000.00'));
    expect(geometry.minLabel).toBe('-250.50');
    expect(geometry.negativeAreas).toHaveLength(1);
    expect(geometry.negativeAreas[0].x).toBeCloseTo(333.33, 1);
    expect(geometry.negativeAreas[0].width).toBeCloseTo(333.33, 1);
    expect(geometry.points.map((point) => point.negative)).toEqual([false, true, false]);
    // Zero lies between the top (max) and the bottom (min).
    expect(geometry.zeroY).toBeGreaterThan(geometry.points[0].y);
    expect(geometry.zeroY).toBeLessThan(geometry.points[1].y);
  });
});
