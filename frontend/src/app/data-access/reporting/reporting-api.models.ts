// Mirrors docs/api/openapi.yaml (TotalAmount, ExcludedWallet schemas).

import { Money } from '../../shared/money/money';

/** A wallet excluded from the total because its currency has no exchange rate (docs/PROGRESS.md, section 5.1). */
export interface ExcludedWallet {
  walletId: string;
  reason: string;
}

export interface TotalAmount {
  /** Requested date; null means "current total" (UC-19). */
  date: string | null;
  amount: Money;
  /** The rate date actually applied during conversion — may differ from the requested date. */
  ratesAsOfDate: string;
  excludedWallets: ExcludedWallet[];
}
