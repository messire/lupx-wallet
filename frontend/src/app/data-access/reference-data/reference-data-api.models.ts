// Mirrors docs/api/openapi.yaml (ReferenceItem, OperationType, Currency,
// AvailableCurrency, OperationBehaviorKind schemas and their *Page variants).

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

/** An active currency — presence in the list means it is active, there is no deactivation. */
export interface Currency {
  id: string;
  code: string;
  name: string;
}

export type CurrencyPage = CursorPage<Currency>;

/** An entry of the currency list provided by the exchange-rate service (GET /currencies/available). */
export interface AvailableCurrency {
  code: string;
  /** English name, as published by the service. */
  name: string;
  /** The currency has already been added to the active ones. */
  isActive: boolean;
}

export interface OperationBehaviorKind {
  id: string;
  code: string;
  name: string;
}
