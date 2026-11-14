import {
  addDays,
  addMonths,
  describeDate,
  formatDateText,
  formatMonthLabel,
  monthGrid,
  monthRange,
  parseDateText,
  shiftMonth,
  weekdayIndex,
} from './calendar-date';

describe('calendar-date', () => {
  it('formats, shifts and spans a yyyy-MM month', () => {
    expect(formatMonthLabel('2026-11')).toBe('Ноябрь 2026');
    expect(shiftMonth('2026-12', 1)).toBe('2027-01');
    expect(shiftMonth('2026-01', -1)).toBe('2025-12');
    expect(monthRange('2028-02')).toEqual({ from: '2028-02-01', to: '2028-02-29' });
    expect(monthRange('2026-11')).toEqual({ from: '2026-11-01', to: '2026-11-30' });
  });

  it('parses дд.мм.гггг (also / and -, single digits) and ISO, rejecting impossible dates', () => {
    expect(parseDateText('14.11.2026')).toBe('2026-11-14');
    expect(parseDateText('1/2/2026')).toBe('2026-02-01');
    expect(parseDateText(' 2026-02-28 ')).toBe('2026-02-28');
    expect(parseDateText('29.02.2025')).toBeNull();
    expect(parseDateText('31.04.2026')).toBeNull();
    expect(parseDateText('14.11')).toBeNull();
    expect(parseDateText('')).toBeNull();
  });

  it('formats ISO as дд.мм.гггг', () => {
    expect(formatDateText('2026-01-05')).toBe('05.01.2026');
    expect(formatDateText('')).toBe('');
    expect(formatDateText(null)).toBe('');
  });

  it('adds days and months across boundaries, clamping to the end of a shorter month', () => {
    expect(addDays('2026-12-31', 1)).toBe('2027-01-01');
    expect(addDays('2026-03-01', -1)).toBe('2026-02-28');
    expect(addMonths('2026-01-31', 1)).toBe('2026-02-28');
    expect(addMonths('2024-03-31', -1)).toBe('2024-02-29');
    expect(addMonths('2026-11-14', -12)).toBe('2025-11-14');
  });

  it('counts weekdays from Monday and builds a Monday-first 6×7 grid', () => {
    expect(weekdayIndex('2026-11-16')).toBe(0); // Monday
    expect(weekdayIndex('2026-11-15')).toBe(6); // Sunday
    const grid = monthGrid('2026-11-14');
    expect(grid.length).toBe(6);
    expect(grid[0][0]).toBe('2026-10-26');
    expect(grid[0][6]).toBe('2026-11-01');
    expect(grid.flat()).toContain('2026-11-30');
  });

  it('describes a day for screen readers', () => {
    expect(describeDate('2026-11-14')).toBe('14 ноября 2026, суббота');
  });
});
