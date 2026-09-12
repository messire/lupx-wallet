// Соответствует docs/api/openapi.yaml (схемы AuditEntry, AuditFieldChange, AuditEntryPage)
// и docs/database/schema.md (комментарий к audit.audit_entries.entity_type).

import { CursorPage } from '../../shared/pagination/cursor-page';

/** Полный список entity_type, поддерживаемых аудитом (docs/database/schema.md). */
export const AUDIT_ENTITY_TYPES = [
  'Wallet',
  'Operation',
  'Transfer',
  'BalanceSnapshot',
  'WalletType',
  'OperationType',
  'Currency',
  'ExchangeRateQuote',
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
