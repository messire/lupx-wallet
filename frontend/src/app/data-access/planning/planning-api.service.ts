import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import {
  PlanStatus,
  PlannedOccurrence,
  PlannedOccurrenceExecuteRequest,
  PlannedOccurrenceExecutionResult,
  PlannedOccurrenceList,
  PlannedTransaction,
  PlannedTransactionPage,
  PlannedTransactionWriteRequest,
} from './planning-api.models';

export interface OccurrenceQuery {
  from: string;
  to: string;
  plannedTransactionId?: string;
}

/** HTTP client of the Planning module (docs/api/openapi.yaml, tag Planning; ADR-0010 §6). */
@Injectable({ providedIn: 'root' })
export class PlanningApiService {
  private readonly baseUrl = environment.apiBaseUrl;

  constructor(private readonly http: HttpClient) {}

  /** Without a status — active and finished plans. */
  list(options: { status?: PlanStatus } & CursorPageOptions = {}): Observable<PlannedTransactionPage> {
    let params = toCursorParams(options);
    if (options.status) {
      params = params.set('status', options.status);
    }
    return this.http.get<PlannedTransactionPage>(`${this.baseUrl}/planned-transactions`, { params });
  }

  create(request: PlannedTransactionWriteRequest): Observable<PlannedTransaction> {
    return this.http.post<PlannedTransaction>(`${this.baseUrl}/planned-transactions`, request);
  }

  update(id: string, request: PlannedTransactionWriteRequest): Observable<PlannedTransaction> {
    return this.http.patch<PlannedTransaction>(`${this.baseUrl}/planned-transactions/${id}`, request);
  }

  finish(id: string): Observable<PlannedTransaction> {
    return this.http.post<PlannedTransaction>(`${this.baseUrl}/planned-transactions/${id}/finish`, {});
  }

  /**
   * 204 (`null` body) — a plan without executions is deleted physically; 200 with the plan — a plan with
   * executions is finished instead (ADR-0010 §8).
   */
  delete(id: string): Observable<PlannedTransaction | null> {
    return this.http.delete<PlannedTransaction | null>(`${this.baseUrl}/planned-transactions/${id}`);
  }

  listOccurrences(query: OccurrenceQuery): Observable<PlannedOccurrenceList> {
    let params = new HttpParams().set('from', query.from).set('to', query.to);
    if (query.plannedTransactionId) {
      params = params.set('plannedTransactionId', query.plannedTransactionId);
    }
    return this.http.get<PlannedOccurrenceList>(`${this.baseUrl}/planned-occurrences`, { params });
  }

  skipOccurrence(planId: string, occurrenceDate: string): Observable<PlannedOccurrence> {
    return this.http.post<PlannedOccurrence>(`${this.occurrenceUrl(planId, occurrenceDate)}/skip`, {});
  }

  unskipOccurrence(planId: string, occurrenceDate: string): Observable<PlannedOccurrence> {
    return this.http.post<PlannedOccurrence>(`${this.occurrenceUrl(planId, occurrenceDate)}/unskip`, {});
  }

  /** Moving to the original date cancels the move. */
  moveOccurrence(planId: string, occurrenceDate: string, date: string): Observable<PlannedOccurrence> {
    return this.http.post<PlannedOccurrence>(`${this.occurrenceUrl(planId, occurrenceDate)}/move`, { date });
  }

  /** `null` returns the occurrence to the plan amount. */
  changeOccurrenceAmount(planId: string, occurrenceDate: string, amount: string | null): Observable<PlannedOccurrence> {
    return this.http.put<PlannedOccurrence>(`${this.occurrenceUrl(planId, occurrenceDate)}/amount`, { amount });
  }

  /** Creates a regular operation in the same transaction and links it with the occurrence (201). */
  executeOccurrence(
    planId: string,
    occurrenceDate: string,
    request: PlannedOccurrenceExecuteRequest,
  ): Observable<PlannedOccurrenceExecutionResult> {
    return this.http.post<PlannedOccurrenceExecutionResult>(`${this.occurrenceUrl(planId, occurrenceDate)}/execute`, request);
  }

  private occurrenceUrl(planId: string, occurrenceDate: string): string {
    return `${this.baseUrl}/planned-transactions/${planId}/occurrences/${occurrenceDate}`;
  }
}
