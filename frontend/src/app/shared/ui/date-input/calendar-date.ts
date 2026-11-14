/**
 * Calendar-date helpers for `app-date-input`. A date is the API string `yyyy-MM-dd` (openapi `format: date`);
 * arithmetic goes through UTC so that the user's time zone and DST never shift a day.
 */

export const MONTH_NAMES = [
  'Январь',
  'Февраль',
  'Март',
  'Апрель',
  'Май',
  'Июнь',
  'Июль',
  'Август',
  'Сентябрь',
  'Октябрь',
  'Ноябрь',
  'Декабрь',
] as const;

const MONTH_NAMES_GENITIVE = [
  'января',
  'февраля',
  'марта',
  'апреля',
  'мая',
  'июня',
  'июля',
  'августа',
  'сентября',
  'октября',
  'ноября',
  'декабря',
] as const;

/** Monday-first, as in the Russian calendar. */
export const WEEKDAY_SHORT = ['Пн', 'Вт', 'Ср', 'Чт', 'Пт', 'Сб', 'Вс'] as const;
export const WEEKDAY_LONG = [
  'понедельник',
  'вторник',
  'среда',
  'четверг',
  'пятница',
  'суббота',
  'воскресенье',
] as const;

const ISO_PATTERN = /^(\d{4})-(\d{2})-(\d{2})$/;
const RU_PATTERN = /^(\d{1,2})[./-](\d{1,2})[./-](\d{4})$/;

function pad(value: number): string {
  return String(value).padStart(2, '0');
}

function isRealDate(year: number, month: number, day: number): boolean {
  const date = new Date(Date.UTC(year, month - 1, day));
  return (
    date.getUTCFullYear() === year && date.getUTCMonth() === month - 1 && date.getUTCDate() === day
  );
}

/** `yyyy-MM-dd` from parts (month 1..12). */
export function toIsoDate(year: number, month: number, day: number): string {
  return `${String(year).padStart(4, '0')}-${pad(month)}-${pad(day)}`;
}

/**
 * Parses what a user typed: `дд.мм.гггг` (also with `/` or `-`, single-digit day/month allowed) or the ISO
 * `гггг-мм-дд`. Returns `yyyy-MM-dd`, or `null` when the text is not a real calendar date.
 */
export function parseDateText(text: string): string | null {
  const trimmed = text.trim();
  const iso = ISO_PATTERN.exec(trimmed);
  const ru = iso ? null : RU_PATTERN.exec(trimmed);
  const [year, month, day] = iso
    ? [Number(iso[1]), Number(iso[2]), Number(iso[3])]
    : ru
      ? [Number(ru[3]), Number(ru[2]), Number(ru[1])]
      : [NaN, NaN, NaN];
  if (!Number.isInteger(year) || !isRealDate(year, month, day)) {
    return null;
  }
  return toIsoDate(year, month, day);
}

/** `yyyy-MM-dd` → `дд.мм.гггг`; anything else is returned as is. */
export function formatDateText(isoDate: string | null | undefined): string {
  const match = isoDate ? ISO_PATTERN.exec(isoDate) : null;
  return match ? `${match[3]}.${match[2]}.${match[1]}` : (isoDate ?? '');
}

/** `yyyy-MM-dd` → "14 ноября 2026, суббота" for a day button's accessible name. */
export function describeDate(isoDate: string): string {
  const { year, month, day } = splitIsoDate(isoDate);
  const weekday = WEEKDAY_LONG[weekdayIndex(isoDate)];
  return `${day} ${MONTH_NAMES_GENITIVE[month - 1]} ${year}, ${weekday}`;
}

export function splitIsoDate(isoDate: string): { year: number; month: number; day: number } {
  const [year, month, day] = isoDate.split('-').map(Number);
  return { year, month, day };
}

export function addDays(isoDate: string, days: number): string {
  const { year, month, day } = splitIsoDate(isoDate);
  const date = new Date(Date.UTC(year, month - 1, day + days));
  return toIsoDate(date.getUTCFullYear(), date.getUTCMonth() + 1, date.getUTCDate());
}

/** Same day `months` later, clamped to the last day of the target month (31 Jan + 1 month = 28/29 Feb). */
export function addMonths(isoDate: string, months: number): string {
  const { year, month, day } = splitIsoDate(isoDate);
  const target = new Date(Date.UTC(year, month - 1 + months, 1));
  const lastDay = new Date(
    Date.UTC(target.getUTCFullYear(), target.getUTCMonth() + 1, 0),
  ).getUTCDate();
  return toIsoDate(target.getUTCFullYear(), target.getUTCMonth() + 1, Math.min(day, lastDay));
}

/** 0 = Monday … 6 = Sunday. */
export function weekdayIndex(isoDate: string): number {
  const { year, month, day } = splitIsoDate(isoDate);
  return (new Date(Date.UTC(year, month - 1, day)).getUTCDay() + 6) % 7;
}

/** Today in the user's local calendar (the same day the native date input offered). */
export function todayIsoDate(): string {
  const now = new Date();
  return toIsoDate(now.getFullYear(), now.getMonth() + 1, now.getDate());
}

/**
 * The 6×7 grid (Monday-first) of the month containing `isoDate`, padded with the neighbouring months' days.
 */
export function monthGrid(isoDate: string): string[][] {
  const { year, month } = splitIsoDate(isoDate);
  const first = toIsoDate(year, month, 1);
  let cursor = addDays(first, -weekdayIndex(first));
  const weeks: string[][] = [];
  for (let week = 0; week < 6; week++) {
    const days: string[] = [];
    for (let weekday = 0; weekday < 7; weekday++) {
      days.push(cursor);
      cursor = addDays(cursor, 1);
    }
    weeks.push(days);
  }
  return weeks;
}
