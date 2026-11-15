import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import {
  Budget,
  BudgetCreateRequest,
  BudgetExecution,
  BudgetPage,
  ExpenseCategory,
  ExpenseCategoryCreateRequest,
  ExpenseCategoryPage,
  ExpenseCategoryUpdateRequest,
} from './budgeting-api.models';

/** HTTP client of the Budgeting module (docs/api/openapi.yaml, tags ExpenseCategories, Budgets; ADR-0010 §5). */
@Injectable({ providedIn: 'root' })
export class BudgetingApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  listCategories(options: { includeInactive?: boolean } & CursorPageOptions = {}): Observable<ExpenseCategoryPage> {
    let params = toCursorParams(options);
    if (options.includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.http.get<ExpenseCategoryPage>(`${this.baseUrl}/expense-categories`, { params });
  }

  createCategory(request: ExpenseCategoryCreateRequest): Observable<ExpenseCategory> {
    return this.http.post<ExpenseCategory>(`${this.baseUrl}/expense-categories`, request);
  }

  updateCategory(id: string, request: ExpenseCategoryUpdateRequest): Observable<ExpenseCategory> {
    return this.http.patch<ExpenseCategory>(`${this.baseUrl}/expense-categories/${id}`, request);
  }

  deactivateCategory(id: string): Observable<ExpenseCategory> {
    return this.http.post<ExpenseCategory>(`${this.baseUrl}/expense-categories/${id}/deactivate`, {});
  }

  /** 409 while a budget limit uses the category — deactivate instead. */
  deleteCategory(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/expense-categories/${id}`);
  }

  listBudgets(options: { month?: string } & CursorPageOptions = {}): Observable<BudgetPage> {
    let params = toCursorParams(options);
    if (options.month) {
      params = params.set('month', options.month);
    }
    return this.http.get<BudgetPage>(`${this.baseUrl}/budgets`, { params });
  }

  /** 409 — a budget for this (month, currency) pair already exists. */
  createBudget(request: BudgetCreateRequest): Observable<Budget> {
    return this.http.post<Budget>(`${this.baseUrl}/budgets`, request);
  }

  deleteBudget(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/budgets/${id}`);
  }

  setLimit(budgetId: string, categoryId: string, limit: string): Observable<Budget> {
    return this.http.put<Budget>(`${this.baseUrl}/budgets/${budgetId}/limits/${categoryId}`, { limit });
  }

  removeLimit(budgetId: string, categoryId: string): Observable<Budget> {
    return this.http.delete<Budget>(`${this.baseUrl}/budgets/${budgetId}/limits/${categoryId}`);
  }

  getExecution(budgetId: string): Observable<BudgetExecution> {
    return this.http.get<BudgetExecution>(`${this.baseUrl}/budgets/${budgetId}/execution`);
  }
}
