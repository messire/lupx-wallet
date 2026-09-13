import { AbstractControl, ValidationErrors, ValidatorFn } from '@angular/forms';

/**
 * Money (docs/api/openapi.yaml, схема Money) — ADR-0005: денежная сумма передается
 * строкой с точкой в качестве десятичного разделителя, произвольная точность.
 *
 * ВАЖНО: сумму нельзя приводить к JS `number` ни при вычислениях, ни при отображении —
 * это может незаметно потерять точность для сумм с большим числом знаков после запятой.
 * Единственные допустимые операции над `amount` в этом файле — строковые: валидация
 * форматом, парсинг ввода как строки и форматирование для отображения.
 */
export interface Money {
  amount: string;
  currencyId: string;
}

const AMOUNT_PATTERN = /^-?\d+(\.\d+)?$/;

/** true, если строка — валидная денежная сумма в формате контракта (ADR-0005). */
export function isValidAmount(value: string): boolean {
  return AMOUNT_PATTERN.test(value.trim());
}

/**
 * Нормализует пользовательский ввод суммы (обрезка пробелов, запятая как
 * десятичный разделитель — частая опечатка при вводе). Возвращает `null`,
 * если результат не является валидной суммой по ADR-0005.
 */
export function parseAmountInput(raw: string): string | null {
  const normalized = raw.trim().replace(',', '.');
  return isValidAmount(normalized) ? normalized : null;
}

/** Reactive Forms валидатор суммы на основе {@link isValidAmount}. */
export const amountValidator: ValidatorFn = (control: AbstractControl): ValidationErrors | null => {
  const value = control.value;
  if (value === null || value === undefined || value === '') {
    // Обязательность поля — забота Validators.required, этот валидатор только про формат.
    return null;
  }
  return isValidAmount(String(value)) ? null : { amount: true };
};

/**
 * Форматирует денежную сумму для отображения (разделитель разрядов), не
 * приводя её к `number` — точность строки сохраняется как есть.
 */
export function formatAmount(amount: string): string {
  const negative = amount.startsWith('-');
  const unsigned = negative ? amount.slice(1) : amount;
  const [integerPart, decimalPart] = unsigned.split('.');
  const withSeparators = integerPart.replace(/\B(?=(\d{3})+(?!\d))/g, ' ');
  const sign = negative ? '-' : '';
  return decimalPart !== undefined ? `${sign}${withSeparators}.${decimalPart}` : `${sign}${withSeparators}`;
}

/** Форматирует {@link Money} целиком: сумма + опциональный код валюты. */
export function formatMoney(money: Money, currencyCode?: string): string {
  const formatted = formatAmount(money.amount);
  return currencyCode ? `${formatted} ${currencyCode}` : formatted;
}
