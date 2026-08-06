// Соответствует docs/api/openapi.yaml (схемы ReferenceItem, OperationType, Currency,
// OperationBehaviorKind и их *Page варианты).

import { CursorPage } from '../../core/api/cursor-page';

export interface ReferenceItem {
  id: string;
  name: string;
  isActive: boolean;
}

export type ReferenceItemPage = CursorPage<ReferenceItem>;

export interface OperationType {
  id: string;
  name: string;
  behaviorKindId: string;
  isActive: boolean;
}

export type OperationTypePage = CursorPage<OperationType>;

export interface Currency {
  id: string;
  code: string;
  name: string;
  isActive: boolean;
}

export type CurrencyPage = CursorPage<Currency>;

export interface OperationBehaviorKind {
  id: string;
  code: string;
  name: string;
}
