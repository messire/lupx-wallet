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
