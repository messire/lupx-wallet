import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { toCursorParams } from '../../core/api/cursor-page';
import { Currency, CurrencyPage, OperationBehaviorKind, OperationType, OperationTypePage, ReferenceItem, ReferenceItemPage } from './reference-data.models';

/**
 * Единая точка HTTP-доступа к справочникам (docs/api/openapi.yaml: /wallet-types,
 * /operation-types, /currencies, /operation-behavior-kinds). Переиспользуется как
 * мини-формами быстрого добавления в features/wallets, так и полноценным экраном
 * управления справочниками features/reference-data (W1.4, docs/PROGRESS.md) — HTTP-вызовы
 * не дублируются ни в одном из потребителей.
 */
@Injectable({ providedIn: 'root' })
export class ReferenceDataService {
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
