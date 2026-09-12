// Соответствует docs/api/openapi.yaml (схемы BalanceSnapshot, BalanceSnapshotPage).

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export interface BalanceSnapshot {
  walletId: string;
  date: string;
  balance: Money;
}

export type BalanceSnapshotPage = CursorPage<BalanceSnapshot>;
