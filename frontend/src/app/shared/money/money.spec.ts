import { FormControl } from '@angular/forms';
import {
  addAmounts,
  amountValidator,
  compareAmounts,
  formatAmount,
  formatMoney,
  isPositiveAmount,
  isValidAmount,
  isZeroAmount,
  nonNegativeAmountValidator,
  parseAmountInput,
  positiveAmountValidator,
  ratioToPercent,
  subtractAmounts,
} from './money';

describe('addAmounts', () => {
  it('adds exactly, keeping the larger scale', () => {
    expect(addAmounts('0.1', '0.2')).toBe('0.3');
    expect(addAmounts('100', '-0.50')).toBe('99.50');
    expect(addAmounts('-1', '-1')).toBe('-2');
  });
});

describe('compareAmounts', () => {
  it('compares exactly, independent of the number of decimal digits', () => {
    expect(compareAmounts('100', '100.00')).toBe(0);
    expect(compareAmounts('99.99', '100')).toBe(-1);
    expect(compareAmounts('100.01', '100')).toBe(1);
    expect(compareAmounts('-1', '0')).toBe(-1);
    expect(compareAmounts('12345678901234567890.000000000000000002', '12345678901234567890.000000000000000001')).toBe(1);
  });

  it('isPositiveAmount rejects zero, negatives and garbage', () => {
    expect(isPositiveAmount('0.01')).toBe(true);
    expect(isPositiveAmount('0.00')).toBe(false);
    expect(isPositiveAmount('-5')).toBe(false);
    expect(isPositiveAmount('abc')).toBe(false);
  });
});

describe('positiveAmountValidator / nonNegativeAmountValidator', () => {
  it('positive: rejects zero and negatives, ignores empty and malformed values', () => {
    expect(positiveAmountValidator(new FormControl('10'))).toBeNull();
    expect(positiveAmountValidator(new FormControl('0.00'))).toEqual({ positiveAmount: true });
    expect(positiveAmountValidator(new FormControl('-1'))).toEqual({ positiveAmount: true });
    expect(positiveAmountValidator(new FormControl(''))).toBeNull();
    expect(positiveAmountValidator(new FormControl('abc'))).toBeNull();
  });

  it('non-negative: allows zero, rejects negatives', () => {
    expect(nonNegativeAmountValidator(new FormControl('0'))).toBeNull();
    expect(nonNegativeAmountValidator(new FormControl('-0.01'))).toEqual({ negativeAmount: true });
  });
});

describe('ratioToPercent', () => {
  it('turns a decimal ratio into a whole percentage rounded down', () => {
    expect(ratioToPercent('0')).toBe('0');
    expect(ratioToPercent('0.355')).toBe('35');
    expect(ratioToPercent('0.5')).toBe('50');
    expect(ratioToPercent('1')).toBe('100');
    expect(ratioToPercent('1.2')).toBe('120');
    expect(ratioToPercent('0.0099')).toBe('0');
  });

  it('returns "0" for invalid or negative input', () => {
    expect(ratioToPercent('-0.5')).toBe('0');
    expect(ratioToPercent('n/a')).toBe('0');
  });
});

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

describe('isZeroAmount', () => {
  it('recognises zero in every written form', () => {
    for (const amount of ['0', '0.00', '-0', '-0.000', '000.0']) {
      expect(isZeroAmount(amount), amount).toBe(true);
    }
  });

  it('rejects non-zero and malformed amounts', () => {
    for (const amount of ['0.01', '-0.01', '10', '', 'abc', '0.']) {
      expect(isZeroAmount(amount), amount).toBe(false);
    }
  });
});

describe('subtractAmounts', () => {
  it('subtracts with the larger number of decimal digits kept', () => {
    expect(subtractAmounts('350.00', '50')).toBe('300.00');
    expect(subtractAmounts('100', '0.5')).toBe('99.5');
    expect(subtractAmounts('0.30', '0.1')).toBe('0.20');
  });

  it('handles negative operands and negative results', () => {
    expect(subtractAmounts('100', '-50.00')).toBe('150.00');
    expect(subtractAmounts('-100.00', '50')).toBe('-150.00');
    expect(subtractAmounts('10', '350.5')).toBe('-340.5');
    expect(subtractAmounts('0.5', '0.75')).toBe('-0.25');
  });

  it('returns a plain zero without a sign when the operands are equal', () => {
    expect(subtractAmounts('5.00', '5')).toBe('0.00');
    expect(subtractAmounts('-5', '-5')).toBe('0');
  });

  it('is exact where a double would lose precision', () => {
    expect(subtractAmounts('0.3', '0.1')).toBe('0.2');
    expect(subtractAmounts('12345678901234567890.123456789012345678', '0.000000000000000001')).toBe(
      '12345678901234567890.123456789012345677',
    );
  });

  it('keeps leading zeros of the fraction', () => {
    expect(subtractAmounts('1', '0.995')).toBe('0.005');
  });
});
