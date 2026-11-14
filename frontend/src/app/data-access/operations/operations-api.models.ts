// Mirrors docs/api/openapi.yaml (Operation, OperationCreateRequest,
// OperationUpdateRequest, StatedBalanceRequest, OperationPage schemas).

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

/** The only adjustment mode: a signed change of the balance (ADR-0003 §6). */
export type AdjustmentMode = 'Delta';

export interface Operation {
  id: string;
  walletId: string;
  /** Null only for a correction amount (`isCorrection = true`). */
  operationTypeId: string | null;
  /** For a correction amount: the signed amount calculated by the system. */
  amount: Money;
  operationDate: string;
  /** Set only when operationType references behaviorKind = Adjustment (Q3). */
  adjustmentMode: AdjustmentMode | null;
  /** ADR-0003 §6: a system record that reconciles the operations with a stated balance.
   *  It cannot be edited, deleted or restored (409); the client derives its title from the amount sign. */
  isCorrection: boolean;
  /** The balance at the end of `operationDate` stated by the user; set only for a correction amount. */
  statedBalance: Money | null;
  /** Non-null means the operation is part of a transfer (see UC-17): it cannot be
   *  edited/deleted directly via /operations, only via /transfers/{transferId}. */
  transferId: string | null;
  /** ADR-0002 §3: a soft-deleted operation stays in the list (excluded from balance
   *  calculations), cannot be edited (409) and can be restored. */
  isDeleted: boolean;
  /** Moment of soft deletion (ISO date-time); null while the operation is active. */
  deletedAt: string | null;
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

/** StatedBalanceRequest (openapi.yaml): the balance at the end of the day from the path, in the wallet currency. */
export interface StatedBalanceRequest {
  /** Decimal-point string; a negative value is allowed. */
  amount: string;
}

/** Result of `PUT /wallets/{walletId}/stated-balances/{date}`. */
export interface StatedBalanceResult {
  /** The correction amount of that date (may be zero). */
  correction: Operation;
  /** True for 201 (stated for the first time), false for 200 (a previous value was replaced). */
  created: boolean;
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
