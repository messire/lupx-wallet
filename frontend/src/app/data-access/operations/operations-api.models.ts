// Mirrors docs/api/openapi.yaml (Operation, OperationCreateRequest,
// OperationUpdateRequest, OperationPage schemas).

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export type AdjustmentMode = 'Absolute' | 'Delta';

export interface Operation {
  id: string;
  walletId: string;
  operationTypeId: string;
  amount: Money;
  operationDate: string;
  /** Set only when operationType references behaviorKind = Adjustment (Q3). */
  adjustmentMode: AdjustmentMode | null;
  /** Non-null means the operation is part of a transfer (see UC-17): it cannot be
   *  edited/deleted directly via /operations, only via /transfers/{transferId}. */
  transferId: string | null;
  createdAt: string;
  updatedAt: string;
}

export type OperationPage = CursorPage<Operation>;

export interface CreateOperationRequest {
  walletId: string;
  operationTypeId: string;
  /** Decimal-point string, arbitrary precision (ADR-0005) — see shared/money/money.ts. */
  amount: string;
  operationDate: string;
  adjustmentMode?: AdjustmentMode;
}

/** OperationUpdateRequest (openapi.yaml) — walletId allows moving the operation to another
 *  wallet (UC-13): the balances of both the source and target wallet are recalculated on
 *  the backend. Not available for operations that are part of a transfer (409). */
export interface UpdateOperationRequest {
  walletId: string;
  operationTypeId?: string;
  amount?: string;
  operationDate?: string;
  adjustmentMode?: AdjustmentMode;
}
