// Mirrors docs/api/openapi.yaml (Wallet, WalletCreateRequest, WalletPage, Money schemas).

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export type { Money };

export interface Wallet {
  id: string;
  name: string;
  walletTypeId: string;
  purposeDescription: string | null;
  currencyId: string;
  initialBalance: Money;
  accountingStartDate: string;
  currentBalance: Money;
  includeInTotal: boolean;
  isPrimary: boolean;
  isArchived: boolean;
  displayOrder: number;
  color: string | null;
  icon: string | null;
  createdAt: string;
  updatedAt: string;
}

export type WalletPage = CursorPage<Wallet>;

export interface CreateWalletRequest {
  name: string;
  walletTypeId: string;
  currencyId: string;
  /** Decimal-point string, arbitrary precision (ADR-0005) — see shared/money/money.ts. */
  initialBalanceAmount: string;
  accountingStartDate: string;
  purposeDescription?: string;
  includeInTotal?: boolean;
  displayOrder?: number;
  color?: string;
  icon?: string;
}

/**
 * PATCH /wallets/{id} (UC-02) — no currency field (currency changes via a
 * separate endpoint, see {@link ChangeWalletCurrencyRequest}). includeInTotal
 * is ignored by the backend (stays true) for the primary wallet (Q8) —
 * silently ignored, not a 409.
 */
export interface UpdateWalletRequest {
  name?: string;
  walletTypeId?: string;
  purposeDescription?: string | null;
  includeInTotal?: boolean;
  displayOrder?: number;
  color?: string | null;
  icon?: string | null;
}

/** PUT /wallets/{id}/currency (UC-06) — 409 if the wallet already has history (Q15). */
export interface ChangeWalletCurrencyRequest {
  currencyId: string;
}
