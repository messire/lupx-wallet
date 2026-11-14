import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable, map } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import {
  CreateOperationRequest,
  Operation,
  OperationPage,
  StatedBalanceRequest,
  StatedBalanceResult,
  UpdateOperationRequest,
} from './operations-api.models';

export interface OperationListFilters extends CursorPageOptions {
  walletId?: string;
  operationTypeId?: string;
  dateFrom?: string;
  dateTo?: string;
}

@Injectable({ providedIn: 'root' })
export class OperationsApiService {
  constructor(private readonly http: HttpClient) {}

  list(filters: OperationListFilters = {}): Observable<OperationPage> {
    let params = new HttpParams();
    if (filters.walletId) {
      params = params.set('walletId', filters.walletId);
    }
    if (filters.operationTypeId) {
      params = params.set('operationTypeId', filters.operationTypeId);
    }
    if (filters.dateFrom) {
      params = params.set('dateFrom', filters.dateFrom);
    }
    if (filters.dateTo) {
      params = params.set('dateTo', filters.dateTo);
    }
    params = toCursorParams(filters, params);

    return this.http.get<OperationPage>(`${environment.apiBaseUrl}/operations`, { params });
  }

  create(request: CreateOperationRequest): Observable<Operation> {
    return this.http.post<Operation>(`${environment.apiBaseUrl}/operations`, request);
  }

  update(operationId: string, request: UpdateOperationRequest): Observable<Operation> {
    return this.http.patch<Operation>(`${environment.apiBaseUrl}/operations/${operationId}`, request);
  }

  /** Soft delete (ADR-0002 §3): any date; the record stays in the list as `isDeleted`. */
  delete(operationId: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/operations/${operationId}`);
  }

  /**
   * States the wallet balance at the end of `date` (ADR-0003 §6). A repeated call for the same date replaces
   * the previous value: 201 means the balance was stated for the first time, 200 that it was replaced.
   */
  setStatedBalance(walletId: string, date: string, amount: string): Observable<StatedBalanceResult> {
    const body: StatedBalanceRequest = { amount };
    return this.http
      .put<Operation>(`${environment.apiBaseUrl}/wallets/${walletId}/stated-balances/${date}`, body, { observe: 'response' })
      .pipe(map((response) => ({ correction: response.body as Operation, created: response.status === 201 })));
  }

  /** Restores a soft-deleted operation (409 if it is not deleted or is part of a transfer). */
  restore(operationId: string): Observable<Operation> {
    return this.http.post<Operation>(`${environment.apiBaseUrl}/operations/${operationId}/restore`, {});
  }
}
