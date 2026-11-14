import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import {
  CashFlowForecast,
  DebtsSummaryReport,
  ForecastHorizon,
  FreeFunds,
  TotalAmount,
  UpcomingPayments,
} from './reporting-api.models';

/**
 * HTTP client of the Reporting module (docs/api/openapi.yaml, tag Reporting; ADR-0010 §7). Every report is computed
 * on request in the primary wallet currency with the latest known rates; without a primary wallet the server
 * answers 404.
 */
@Injectable({ providedIn: 'root' })
export class ReportingApiService {
  private readonly baseUrl = `${environment.apiBaseUrl}/reporting`;

  constructor(private readonly http: HttpClient) {}

  /** Without `date` — the current total (= free funds, В9, UC-19); with `date` — a historical one (UC-21). */
  getTotalAmount(date?: string): Observable<TotalAmount> {
    let params = new HttpParams();
    if (date) {
      params = params.set('date', date);
    }
    return this.http.get<TotalAmount>(`${this.baseUrl}/total-amount`, { params });
  }

  getFreeFunds(): Observable<FreeFunds> {
    return this.http.get<FreeFunds>(`${this.baseUrl}/free-funds`);
  }

  /** Planned expenses and unpaid loan schedule items up to today + `days` (1…366), overdue ones included. */
  getUpcomingPayments(days = 30): Observable<UpcomingPayments> {
    return this.http.get<UpcomingPayments>(`${this.baseUrl}/upcoming-payments`, {
      params: new HttpParams().set('days', days),
    });
  }

  getDebtsSummary(): Observable<DebtsSummaryReport> {
    return this.http.get<DebtsSummaryReport>(`${this.baseUrl}/debts-summary`);
  }

  getCashFlowForecast(months: ForecastHorizon = 3): Observable<CashFlowForecast> {
    return this.http.get<CashFlowForecast>(`${this.baseUrl}/cash-flow-forecast`, {
      params: new HttpParams().set('months', months),
    });
  }
}
