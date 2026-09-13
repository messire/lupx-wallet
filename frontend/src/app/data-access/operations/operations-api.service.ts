import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import { CreateOperationRequest, Operation, OperationPage, UpdateOperationRequest } from './operations-api.models';

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

  delete(operationId: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/operations/${operationId}`);
  }
}
