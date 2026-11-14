// Mirrors docs/api/openapi.yaml (AuditEntry, AuditFieldChange, AuditEntryPage schemas)
// and docs/database/schema.md (comment on audit.audit_entries.entity_type).

import { CursorPage } from '../../shared/pagination/cursor-page';

/** Full list of entity_type values supported by audit (docs/database/schema.md). */
export const AUDIT_ENTITY_TYPES = [
  'Wallet',
  'Operation',
  'Transfer',
  'BalanceSnapshot',
  'WalletType',
  'OperationType',
  'Currency',
  'ExchangeRateQuote',
  // v0.4 (ADR-0010): financial planning aggregates.
  'Creditor',
  'Debt',
  'Loan',
  'Fund',
  'ExpenseCategory',
  'Budget',
  'PlannedTransaction',
] as const;

export type AuditEntityType = (typeof AUDIT_ENTITY_TYPES)[number];

export type AuditActorKind = 'User' | 'System';

export interface AuditFieldChange {
  field: string;
  oldValue: string | null;
  newValue: string | null;
}

export interface AuditEntry {
  id: string;
  entityType: string;
  entityId: string;
  action: string;
  occurredAt: string;
  actorKind: AuditActorKind;
  actorSystemProcess: string | null;
  changes: AuditFieldChange[];
}

export type AuditEntryPage = CursorPage<AuditEntry>;
