// Mirrors docs/api/openapi.yaml (tag Funds — ADR-0010 §4): Fund, FundAllocation, WalletReservation and the
// request schemas. A reservation never changes a wallet balance.

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export type FundStatus = 'Active' | 'Closed';

export interface FundAllocation {
  walletId: string;
  amount: Money;
  /** The wallet's reserve-coverage deficit, limited by this allocation. */
  deficit: Money;
}

export interface Fund {
  id: string;
  name: string;
  currencyId: string;
  targetAmount: Money;
  /** Sum of the allocations. */
  reserved: Money;
  /** reserved / targetAmount as an unrounded decimal string; 1 or more — the target is reached. */
  progress: string;
  /** Sum of the allocations' coverage deficits (0 for a closed fund). */
  deficit: Money;
  linkedDebtId?: string | null;
  linkedLoanId?: string | null;
  status: FundStatus;
  /** The fund has had reserves (even released ones) — it can be closed but not deleted. */
  hasAllocationHistory: boolean;
  /** Non-zero allocations in creation order. */
  allocations: FundAllocation[];
  createdAt: string;
  updatedAt: string;
  closedAt?: string | null;
}

export type FundPage = CursorPage<Fund>;

/** Create and full-replace update share the shape; an omitted link is removed on update. */
export interface FundWriteRequest {
  name: string;
  targetAmount: string;
  currencyId: string;
  linkedDebtId?: string | null;
  linkedLoanId?: string | null;
}

export interface FundAllocationRequest {
  walletId: string;
  /** > 0, in the fund currency. */
  amount: string;
}

/** Per-wallet reservation overview: free = balance − reserved (may be negative); deficit = reserved − balance, if > 0. */
export interface WalletReservation {
  walletId: string;
  currencyId: string;
  balance: Money;
  reserved: Money;
  free: Money;
  deficit: Money;
}
