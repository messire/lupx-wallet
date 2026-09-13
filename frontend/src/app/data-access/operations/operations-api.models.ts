// Соответствует docs/api/openapi.yaml (схемы Operation, OperationCreateRequest,
// OperationUpdateRequest, OperationPage).

import { CursorPage } from '../../shared/pagination/cursor-page';
import { Money } from '../../shared/money/money';

export type AdjustmentMode = 'Absolute' | 'Delta';

export interface Operation {
  id: string;
  walletId: string;
  operationTypeId: string;
  amount: Money;
  operationDate: string;
  /** Заполнено только когда operationType ссылается на behaviorKind = Adjustment (Q3). */
  adjustmentMode: AdjustmentMode | null;
  /** Не null — операция является частью перевода (см. UC-17): редактировать/удалять
   *  напрямую через /operations нельзя, только через /transfers/{transferId}. */
  transferId: string | null;
  createdAt: string;
  updatedAt: string;
}

export type OperationPage = CursorPage<Operation>;

export interface CreateOperationRequest {
  walletId: string;
  operationTypeId: string;
  /** Строка с точкой, произвольная точность (ADR-0005) — см. shared/money/money.ts. */
  amount: string;
  operationDate: string;
  adjustmentMode?: AdjustmentMode;
}

/** OperationUpdateRequest (openapi.yaml) — walletId позволяет перенести операцию на другой
 *  кошелек (UC-13, решение пользователя от 2026-09-11): баланс исходного и нового кошелька
 *  пересчитывается на бэкенде. Недоступно для операций — частей перевода (409). */
export interface UpdateOperationRequest {
  walletId: string;
  operationTypeId?: string;
  amount?: string;
  operationDate?: string;
  adjustmentMode?: AdjustmentMode;
}
