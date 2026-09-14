import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { toCursorParams } from '../../shared/pagination/cursor-page';
import { Currency, CurrencyPage, OperationBehaviorKind, OperationType, OperationTypePage, ReferenceItem, ReferenceItemPage } from './reference-data-api.models';

/**
 * Single HTTP access point for reference data (docs/api/openapi.yaml: /wallet-types,
 * /operation-types, /currencies, /operation-behavior-kinds). Reused both by the quick-add
 * mini-forms in features/wallets and by the full reference-data management screen in
 * features/reference-data — no consumer duplicates these HTTP calls.
 */
@Injectable({ providedIn: 'root' })
export class ReferenceDataApiService {
  constructor(private readonly http: HttpClient) {}

  listWalletTypes(options: { includeInactive?: boolean; limit?: number } = {}): Observable<ReferenceItemPage> {
    return this.http.get<ReferenceItemPage>(`${environment.apiBaseUrl}/wallet-types`, { params: this.toParams(options) });
  }

  createWalletType(name: string): Observable<ReferenceItem> {
    return this.http.post<ReferenceItem>(`${environment.apiBaseUrl}/wallet-types`, { name });
  }

  deactivateWalletType(id: string): Observable<ReferenceItem> {
    return this.http.post<ReferenceItem>(`${environment.apiBaseUrl}/wallet-types/${id}/deactivate`, {});
  }

  deleteWalletType(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/wallet-types/${id}`);
  }

  listOperationTypes(options: { includeInactive?: boolean; behaviorKindId?: string; limit?: number } = {}): Observable<OperationTypePage> {
    let params = this.toParams(options);
    if (options.behaviorKindId) {
      params = params.set('behaviorKindId', options.behaviorKindId);
    }
    return this.http.get<OperationTypePage>(`${environment.apiBaseUrl}/operation-types`, { params });
  }

  createOperationType(name: string, behaviorKindId: string): Observable<OperationType> {
    return this.http.post<OperationType>(`${environment.apiBaseUrl}/operation-types`, { name, behaviorKindId });
  }

  deactivateOperationType(id: string): Observable<OperationType> {
    return this.http.post<OperationType>(`${environment.apiBaseUrl}/operation-types/${id}/deactivate`, {});
  }

  deleteOperationType(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/operation-types/${id}`);
  }

  listCurrencies(options: { includeInactive?: boolean; limit?: number } = {}): Observable<CurrencyPage> {
    return this.http.get<CurrencyPage>(`${environment.apiBaseUrl}/currencies`, { params: this.toParams(options) });
  }

  createCurrency(code: string, name: string): Observable<Currency> {
    return this.http.post<Currency>(`${environment.apiBaseUrl}/currencies`, { code, name });
  }

  deactivateCurrency(id: string): Observable<Currency> {
    return this.http.post<Currency>(`${environment.apiBaseUrl}/currencies/${id}/deactivate`, {});
  }

  deleteCurrency(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/currencies/${id}`);
  }

  listOperationBehaviorKinds(): Observable<OperationBehaviorKind[]> {
    return this.http.get<OperationBehaviorKind[]>(`${environment.apiBaseUrl}/operation-behavior-kinds`);
  }

  private toParams(options: { includeInactive?: boolean; limit?: number }): HttpParams {
    let params = new HttpParams();
    if (options.includeInactive) {
      params = params.set('includeInactive', 'true');
    }
    return toCursorParams(options, params);
  }
}
