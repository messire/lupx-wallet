// Соответствует docs/api/openapi.yaml (схемы BalanceSnapshot, BalanceSnapshotPage).

import { CursorPage } from '../../core/api/cursor-page';
import { Money } from '../../core/money/money';

export interface BalanceSnapshot {
  walletId: string;
  date: string;
  balance: Money;
}

export type BalanceSnapshotPage = CursorPage<BalanceSnapshot>;
