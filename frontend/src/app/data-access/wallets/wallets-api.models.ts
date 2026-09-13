// Соответствует docs/api/openapi.yaml (схемы Wallet, WalletCreateRequest, WalletPage, Money).

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
  /** Строка с точкой, произвольная точность (ADR-0005) — см. shared/money/money.ts. */
  initialBalanceAmount: string;
  accountingStartDate: string;
  purposeDescription?: string;
  includeInTotal?: boolean;
  displayOrder?: number;
  color?: string;
  icon?: string;
}

/**
 * PATCH /wallets/{id} (UC-02) — без валюты (валюта меняется отдельным
 * эндпоинтом, см. {@link ChangeWalletCurrencyRequest}). includeInTotal
 * игнорируется бэкендом (остается true), если кошелек основной (Q8) —
 * не 409, тихое игнорирование.
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

/** PUT /wallets/{id}/currency (UC-06) — 409, если у кошелька уже есть история (Q15). */
export interface ChangeWalletCurrencyRequest {
  currencyId: string;
}
