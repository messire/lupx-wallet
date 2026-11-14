// Mirrors docs/api/openapi.yaml (tags ExpenseCategories, Budgets — ADR-0010 §5): ExpenseCategory, Budget,
// CategoryLimit, BudgetExecution and the request schemas. Budget amounts are plain decimal strings (ADR-0005)
// in the budget currency.

import { CursorPage } from '../../shared/pagination/cursor-page';

export interface ExpenseCategory {
  id: string;
  name: string;
  isActive: boolean;
  /** Operation types with the Expense behavior; a type belongs to at most one category. */
  operationTypeIds: string[];
}

export type ExpenseCategoryPage = CursorPage<ExpenseCategory>;

export interface ExpenseCategoryCreateRequest {
  name: string;
  operationTypeIds?: string[];
}

/** An absent field is not changed; `operationTypeIds` is the full new set. */
export interface ExpenseCategoryUpdateRequest {
  name?: string;
  operationTypeIds?: string[];
}

export interface CategoryLimit {
  expenseCategoryId: string;
  /** In the budget currency, not less than zero. */
  limit: string;
}

export interface Budget {
  id: string;
  /** YYYY-MM. */
  month: string;
  currencyId: string;
  limits: CategoryLimit[];
  createdAt: string;
  updatedAt: string;
}

export type BudgetPage = CursorPage<Budget>;

export interface BudgetCreateRequest {
  month: string;
  currencyId: string;
}

export interface BudgetCategoryExecution {
  expenseCategoryId: string;
  name: string;
  isActive: boolean;
  limit: string;
  actual: string;
  /** limit − actual; negative when overspent. */
  remaining: string;
  isOverspent: boolean;
}

export interface BudgetAppliedRate {
  fromCurrencyId: string;
  operationDate: string;
  rateDate: string;
  rate: string;
  /** No rate on the operation date — the nearest earlier one was applied. */
  isFallback: boolean;
}

export interface BudgetExcludedOperation {
  operationId: string;
  currencyId: string;
  operationDate: string;
  /** In the operation currency. */
  amount: string;
  reason: string;
}

export interface BudgetExecution {
  budgetId: string;
  month: string;
  currencyId: string;
  categories: BudgetCategoryExecution[];
  /** The «Без бюджета» row: expenses of types outside every category with a limit in this budget. */
  unbudgeted: { actual: string };
  totals: {
    limit: string;
    budgetedActual: string;
    remaining: string;
    actual: string;
  };
  rateSource: string;
  appliedRates: BudgetAppliedRate[];
  excludedOperations: BudgetExcludedOperation[];
}
