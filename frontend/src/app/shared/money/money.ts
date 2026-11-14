import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Money (docs/api/openapi.yaml, Money schema) — ADR-0005: a monetary amount is
 * transferred as a string with a dot as the decimal separator, arbitrary precision.
 *
 * IMPORTANT: the amount must never be converted to a JS `number`, neither for
 * calculations nor for display — this can silently lose precision for amounts
 * with many decimal digits. The only operations allowed on `amount` in this file
 * are string-based: format validation, parsing user input, and display formatting.
 */
export interface Money {
  amount: string;
  currencyId: string;
  /** Read-only code of the currency, filled by the server where available (openapi.yaml Money.currencyCode). */
  currencyCode?: string;
}

const AMOUNT_PATTERN = /^-?\d+(\.\d+)?$/;

/** True if the string is a valid monetary amount in the contract format (ADR-0005). */
export function isValidAmount(value: string): boolean {
  return AMOUNT_PATTERN.test(value.trim());
}

/**
 * Normalizes user amount input (trims whitespace, treats a comma as the
 * decimal separator — a common typo when typing). Returns `null` if the
 * result is not a valid amount per ADR-0005.
 */
export function parseAmountInput(raw: string): string | null {
  const normalized = raw.trim().replace(',', '.');
  return isValidAmount(normalized) ? normalized : null;
}

/** Reactive Forms amount validator built on {@link isValidAmount}. */
export const amountValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value;
  if (value === null || value === undefined || value === '') {
    // Required-ness is Validators.required's job; this validator only checks the format.
    return null;
  }
  return isValidAmount(String(value)) ? null : { amount: true };
};

/**
 * Reactive Forms validator: the amount must be strictly greater than zero (`{ positiveAmount: true }`).
 * Format errors are left to {@link amountValidator}; an empty value is left to `Validators.required`.
 */
export const positiveAmountValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value;
  if (value === null || value === undefined || value === '' || !isValidAmount(String(value))) {
    return null;
  }
  return compareAmounts(String(value).trim(), '0') > 0 ? null : { positiveAmount: true };
};

/** Same as {@link positiveAmountValidator}, but zero is allowed (`{ negativeAmount: true }`), e.g. a budget limit. */
export const nonNegativeAmountValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value;
  if (value === null || value === undefined || value === '' || !isValidAmount(String(value))) {
    return null;
  }
  return compareAmounts(String(value).trim(), '0') >= 0 ? null : { negativeAmount: true };
};

/**
 * Formats a monetary amount for display (thousands separators), without
 * converting it to a `number` — the string's precision is preserved as-is.
 */
export function formatAmount(amount: string): string {
  const negative = amount.startsWith('-');
  const unsigned = negative ? amount.slice(1) : amount;
  const [integerPart, decimalPart] = unsigned.split('.');
  const withSeparators = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  const sign = negative ? '-' : '';
  return decimalPart !== undefined ? `${sign}${withSeparators}.${decimalPart}` : `${sign}${withSeparators}`;
}

/** Formats a whole {@link Money}: amount + optional currency code. */
export function formatMoney(money: Money, currencyCode?: string): string {
  const formatted = formatAmount(money.amount);
  return currencyCode ? `${formatted} ${currencyCode}` : formatted;
}

/** True if the string is a valid amount equal to zero ("0", "0.00", "-0.0"). Works on the string, not on a `number`. */
export function isZeroAmount(amount: string): boolean {
  return /^-?0+(\.0+)?$/.test(amount.trim());
}

/**
 * Exact decimal difference `minuend - subtrahend` of two amounts in the contract format (ADR-0005).
 * Computed on scaled `BigInt`s, never through `number`; the result keeps the larger number of decimal digits
 * of the operands ("350.00" - "50" = "300.00"). Both arguments must be valid amounts ({@link isValidAmount}).
 */
export function subtractAmounts(minuend: string, subtrahend: string): string {
  const left = parseDecimal(minuend);
  const right = parseDecimal(subtrahend);
  const scale = Math.max(left.scale, right.scale);
  const difference = scaleUp(left, scale) - scaleUp(right, scale);

  const negative = difference < 0n;
  const digits = (negative ? -difference : difference).toString().padStart(scale + 1, '0');
  const integerPart = digits.slice(0, digits.length - scale);
  const decimalPart = digits.slice(digits.length - scale);
  return `${negative ? '-' : ''}${integerPart}${scale > 0 ? `.${decimalPart}` : ''}`;
}

/** Exact decimal sum of two amounts (ADR-0005), via {@link subtractAmounts}; both must be valid amounts. */
export function addAmounts(left: string, right: string): string {
  const trimmed = right.trim();
  return subtractAmounts(left, trimmed.startsWith('-') ? trimmed.slice(1) : `-${trimmed}`);
}

/**
 * Exact comparison of two amounts in the contract format (ADR-0005): negative if `left < right`,
 * zero if equal, positive if `left > right`. String/`BigInt`-based, never through `number`.
 */
export function compareAmounts(left: string, right: string): number {
  const difference = subtractAmounts(left, right);
  if (isZeroAmount(difference)) {
    return 0;
  }
  return difference.startsWith('-') ? -1 : 1;
}

/** True if the amount is valid and strictly greater than zero (string-based, ADR-0005). */
export function isPositiveAmount(amount: string): boolean {
  return isValidAmount(amount) && compareAmounts(amount, '0') > 0;
}

/**
 * A ratio in the contract's decimal-string format ("0.355", "1.2" — e.g. `Fund.progress`) as a whole
 * percentage rounded down ("35", "120"). Computed by moving the decimal point, not through `number`, so
 * a huge ratio never turns into "Infinity"; an invalid or negative value yields "0".
 */
export function ratioToPercent(ratio: string): string {
  const trimmed = ratio.trim();
  if (!isValidAmount(trimmed) || trimmed.startsWith('-')) {
    return '0';
  }
  const [integerPart, decimalPart = ''] = trimmed.split('.');
  return BigInt(`${integerPart}${decimalPart.padEnd(2, '0').slice(0, 2)}`).toString();
}

interface ParsedDecimal {
  /** Signed integer: the amount multiplied by 10^scale. */
  units: bigint;
  scale: number;
}

function parseDecimal(amount: string): ParsedDecimal {
  const trimmed = amount.trim();
  const negative = trimmed.startsWith('-');
  const [integerPart, decimalPart = ''] = (negative ? trimmed.slice(1) : trimmed).split('.');
  const units = BigInt(integerPart + decimalPart);
  return { units: negative ? -units : units, scale: decimalPart.length };
}

function scaleUp(value: ParsedDecimal, scale: number): bigint {
  return value.units * 10n ** BigInt(scale - value.scale);
}
