// Mirrors docs/api/openapi.yaml (ReferenceItem, OperationType, Currency,
// OperationBehaviorKind schemas and their *Page variants).

import { CursorPage } from '../../shared/pagination/cursor-page';

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
