// Mirrors docs/api/openapi.yaml (Transfer, TransferCreateRequest, TransferPage schemas).

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export interface Transfer {
  id: string;
  sourceWalletId: string;
  targetWalletId: string;
  sourceOperationId: string;
  targetOperationId: string;
  amount: Money;
  transferDate: string;
  createdAt: string;
}

export type TransferPage = CursorPage<Transfer>;

/** TransferCreateRequest (openapi.yaml) — amount is a string without a currency: the
 *  currency comes from the wallets (UC-16, both wallets must share the same currency). */
export interface CreateTransferRequest {
  sourceWalletId: string;
  targetWalletId: string;
  amount: string;
  transferDate: string;
}
