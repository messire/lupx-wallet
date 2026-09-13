// Соответствует docs/api/openapi.yaml (схемы TotalAmount, ExcludedWallet).

import { Money } from '../../shared/money/money';

/** Кошелёк, исключённый из суммы из-за отсутствия курса его валюты (docs/PROGRESS.md, П.5.1). */
export interface ExcludedWallet {
  walletId: string;
  reason: string;
}

export interface TotalAmount {
  /** Запрошенная дата; null означает "текущая сумма" (UC-19). */
  date: string | null;
  amount: Money;
  /** Дата курса, реально применённого при конвертации — может отличаться от запрошенной. */
  ratesAsOfDate: string;
  excludedWallets: ExcludedWallet[];
}
