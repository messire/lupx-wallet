import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import { CreateTransferRequest, Transfer, TransferPage } from './transfers-api.models';

export interface TransferListFilters extends CursorPageOptions {
  /** Кошелек как источник ИЛИ получатель (openapi.yaml). */
  walletId?: string;
}

@Injectable({ providedIn: 'root' })
export class TransfersApiService {
  constructor(private readonly http: HttpClient) {}

  list(filters: TransferListFilters = {}): Observable<TransferPage> {
    let params = new HttpParams();
    if (filters.walletId) {
      params = params.set('walletId', filters.walletId);
    }
    params = toCursorParams(filters, params);

    return this.http.get<TransferPage>(`${environment.apiBaseUrl}/transfers`, { params });
  }

  create(request: CreateTransferRequest): Observable<Transfer> {
    return this.http.post<Transfer>(`${environment.apiBaseUrl}/transfers`, request);
  }

  delete(transferId: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/transfers/${transferId}`);
  }
}
