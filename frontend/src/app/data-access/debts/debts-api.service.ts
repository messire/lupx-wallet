import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import {
  Creditor,
  CreditorPage,
  Debt,
  DebtCreateRequest,
  DebtPage,
  DebtSummary,
  DebtUpdateRequest,
  Loan,
  LoanCreateRequest,
  LoanPage,
  LoanScheduleGenerateRequest,
  LoanScheduleReplaceRequest,
  LoanUpdateRequest,
  PaymentCreateRequest,
} from './debts-api.models';

export interface ObligationListFilters extends CursorPageOptions {
  creditorId?: string;
}

/**
 * HTTP client of the Debts module (docs/api/openapi.yaml, tags Creditors, Debts, Loans; ADR-0010 §3):
 * the creditor dictionary shared by debts and loans, debts with repayments, loans with a schedule and payments.
 * A repayment/payment creates a regular expense operation on the server — the client never creates it separately.
 */
@Injectable({ providedIn: 'root' })
export class DebtsApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  listCreditors(options: { includeInactive?: boolean } & CursorPageOptions = {}): Observable<CreditorPage> {
    let params = toCursorParams(options);
    if (options.includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return this.http.get<CreditorPage>(`${this.baseUrl}/creditors`, { params });
  }

  createCreditor(name: string): Observable<Creditor> {
    return this.http.post<Creditor>(`${this.baseUrl}/creditors`, { name });
  }

  renameCreditor(id: string, name: string): Observable<Creditor> {
    return this.http.patch<Creditor>(`${this.baseUrl}/creditors/${id}`, { name });
  }

  deactivateCreditor(id: string): Observable<Creditor> {
    return this.http.post<Creditor>(`${this.baseUrl}/creditors/${id}/deactivate`, {});
  }

  /** 409 while a debt or a loan uses the creditor (ADR-0002 §4) — deactivate instead. */
  deleteCreditor(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/creditors/${id}`);
  }

  listDebts(filters: ObligationListFilters = {}): Observable<DebtPage> {
    return this.http.get<DebtPage>(`${this.baseUrl}/debts`, { params: this.obligationParams(filters) });
  }

  getDebtSummary(): Observable<DebtSummary> {
    return this.http.get<DebtSummary>(`${this.baseUrl}/debts/summary`);
  }

  getDebt(id: string): Observable<Debt> {
    return this.http.get<Debt>(`${this.baseUrl}/debts/${id}`);
  }

  createDebt(request: DebtCreateRequest): Observable<Debt> {
    return this.http.post<Debt>(`${this.baseUrl}/debts`, request);
  }

  updateDebt(id: string, request: DebtUpdateRequest): Observable<Debt> {
    return this.http.patch<Debt>(`${this.baseUrl}/debts/${id}`, request);
  }

  /** Only a debt without repayments (409 otherwise — cancel it instead, ADR-0010 §8). */
  deleteDebt(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/debts/${id}`);
  }

  cancelDebt(id: string): Observable<Debt> {
    return this.http.post<Debt>(`${this.baseUrl}/debts/${id}/cancel`, {});
  }

  /** 201 with the recalculated debt. */
  recordRepayment(id: string, request: PaymentCreateRequest): Observable<Debt> {
    return this.http.post<Debt>(`${this.baseUrl}/debts/${id}/repayments`, request);
  }

  listLoans(filters: ObligationListFilters = {}): Observable<LoanPage> {
    return this.http.get<LoanPage>(`${this.baseUrl}/loans`, { params: this.obligationParams(filters) });
  }

  getLoan(id: string): Observable<Loan> {
    return this.http.get<Loan>(`${this.baseUrl}/loans/${id}`);
  }

  createLoan(request: LoanCreateRequest): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans`, request);
  }

  updateLoan(id: string, request: LoanUpdateRequest): Observable<Loan> {
    return this.http.patch<Loan>(`${this.baseUrl}/loans/${id}`, request);
  }

  deleteLoan(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/loans/${id}`);
  }

  cancelLoan(id: string): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans/${id}/cancel`, {});
  }

  /** Replaces the whole schedule (move a date, change amounts, add/remove items). */
  replaceSchedule(id: string, request: LoanScheduleReplaceRequest): Observable<Loan> {
    return this.http.put<Loan>(`${this.baseUrl}/loans/${id}/schedule`, request);
  }

  /** N equal monthly payments, replacing the current schedule. */
  generateSchedule(id: string, request: LoanScheduleGenerateRequest): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans/${id}/schedule/generate`, request);
  }

  recordLoanPayment(id: string, request: PaymentCreateRequest): Observable<Loan> {
    return this.http.post<Loan>(`${this.baseUrl}/loans/${id}/payments`, request);
  }

  private obligationParams(filters: ObligationListFilters): HttpParams {
    let params = toCursorParams(filters);
    if (filters.creditorId) {
      params = params.set('creditorId', filters.creditorId);
    }
    return params;
  }
}
