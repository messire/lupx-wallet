// Соответствует docs/api/openapi.yaml (схемы Transfer, TransferCreateRequest, TransferPage).

import { CursorPage } from '../../core/api/cursor-page';
import { Money } from '../../core/money/money';

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

/** TransferCreateRequest (openapi.yaml) — amount строкой без валюты: валюта берется
 *  из кошельков (UC-16, оба кошелька обязаны быть одной валюты). */
export interface CreateTransferRequest {
  sourceWalletId: string;
  targetWalletId: string;
  amount: string;
  transferDate: string;
}
