// Соответствует docs/api/openapi.yaml (схемы Wallet, WalletCreateRequest, WalletPage, Money).

export interface Money {
  amount: string;
  currencyId: string;
}

export interface Wallet {
  id: string;
  name: string;
  walletTypeId: string;
  purposeDescription: string | null;
  currencyId: string;
  initialBalance: Money;
  accountingStartDate: string;
  currentBalance: Money;
  includeInTotal: boolean;
  isPrimary: boolean;
  isArchived: boolean;
  displayOrder: number;
  color: string | null;
  icon: string | null;
  createdAt: string;
  updatedAt: string;
}

export interface CursorPageMeta {
  nextCursor: string | null;
  hasMore: boolean;
}

export interface WalletPage {
  data: Wallet[];
  pagination: CursorPageMeta;
}

export interface CreateWalletRequest {
  name: string;
  walletTypeId: string;
  currencyId: string;
  initialBalanceAmount: number;
  accountingStartDate: string;
  purposeDescription?: string;
  includeInTotal?: boolean;
  displayOrder?: number;
  color?: string;
  icon?: string;
}
