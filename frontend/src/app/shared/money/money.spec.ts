import { amountValidator, formatAmount, formatMoney, isValidAmount, parseAmountInput } from './money';

describe('isValidAmount', () => {
  it('accepts a plain integer', () => {
    expect(isValidAmount('100')).toBe(true);
  });

  it('accepts a decimal amount', () => {
    expect(isValidAmount('100.50')).toBe(true);
  });

  it('accepts a negative amount', () => {
    expect(isValidAmount('-42.5')).toBe(true);
  });

  it('accepts arbitrary decimal precision without limit (ADR-0005)', () => {
    expect(isValidAmount('0.123456789012345678')).toBe(true);
  });

  it('accepts surrounding whitespace by trimming', () => {
    expect(isValidAmount('  100.50  ')).toBe(true);
  });

  it('rejects an empty string', () => {
    expect(isValidAmount('')).toBe(false);
  });

  it('rejects a comma as decimal separator', () => {
    expect(isValidAmount('100,50')).toBe(false);
  });

  it('rejects a trailing decimal point with no digits', () => {
    expect(isValidAmount('100.')).toBe(false);
  });

  it('rejects a leading decimal point with no integer part', () => {
    expect(isValidAmount('.50')).toBe(false);
  });

  it('rejects non-numeric input', () => {
    expect(isValidAmount('abc')).toBe(false);
  });

  it('rejects a double negative sign', () => {
    expect(isValidAmount('--5')).toBe(false);
  });

  it('rejects a plus sign', () => {
    expect(isValidAmount('+5')).toBe(false);
  });
});

describe('parseAmountInput', () => {
  it('returns the trimmed value unchanged when already valid', () => {
    expect(parseAmountInput('  100.50  ')).toBe('100.50');
  });

  it('normalizes a comma decimal separator to a dot', () => {
    expect(parseAmountInput('100,50')).toBe('100.50');
  });

  it('preserves arbitrary precision without rounding (ADR-0005)', () => {
    expect(parseAmountInput('0,123456789012345678')).toBe('0.123456789012345678');
  });

  it('preserves the negative sign', () => {
    expect(parseAmountInput('-42,5')).toBe('-42.5');
  });

  it('returns null for an empty string', () => {
    expect(parseAmountInput('')).toBeNull();
  });

  it('returns null for input that is still invalid after normalization', () => {
    expect(parseAmountInput('abc')).toBeNull();
  });

  it('returns null when there is more than one comma/decimal separator', () => {
    expect(parseAmountInput('1,2,3')).toBeNull();
  });
});

describe('amountValidator', () => {
  function control(value: unknown) {
    return { value } as any;
  }

  it('returns null (valid) for an empty value — required is a separate validator', () => {
    expect(amountValidator(control(''))).toBeNull();
    expect(amountValidator(control(null))).toBeNull();
    expect(amountValidator(control(undefined))).toBeNull();
  });

  it('returns null (valid) for a well-formed amount string', () => {
    expect(amountValidator(control('100.50'))).toBeNull();
  });

  it('returns an amount validation error for a malformed amount string', () => {
    expect(amountValidator(control('100,50'))).toEqual({ amount: true });
  });

  it('coerces a non-string control value to string before validating', () => {
    expect(amountValidator(control(100))).toBeNull();
  });
});

// formatAmount separates thousands with a non-breaking space (U+00A0), not a regular one.
const NBSP = ' ';

describe('formatAmount', () => {
  it('adds thousands separators to the integer part', () => {
    expect(formatAmount('1000000')).toBe(`1${NBSP}000${NBSP}000`);
  });

  it('keeps the decimal part untouched (no rounding)', () => {
    expect(formatAmount('1000000.123456789012345678')).toBe(`1${NBSP}000${NBSP}000.123456789012345678`);
  });

  it('formats a negative amount, keeping the sign before the separators', () => {
    expect(formatAmount('-1000000.5')).toBe(`-1${NBSP}000${NBSP}000.5`);
  });

  it('does not add separators for amounts under 1000', () => {
    expect(formatAmount('999')).toBe('999');
  });

  it('formats zero as-is', () => {
    expect(formatAmount('0')).toBe('0');
  });

  it('does not add a trailing dot for integer amounts', () => {
    expect(formatAmount('1000')).toBe(`1${NBSP}000`);
  });
});

describe('formatMoney', () => {
  it('formats the amount alone when no currency code is provided', () => {
    expect(formatMoney({ amount: '1000.50', currencyId: 'cur-1' })).toBe(`1${NBSP}000.50`);
  });

  it('appends the currency code when provided', () => {
    expect(formatMoney({ amount: '1000.50', currencyId: 'cur-1' }, 'USD')).toBe(`1${NBSP}000.50 USD`);
  });
});
