import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../core/api/cursor-page';
import { ChangeWalletCurrencyRequest, CreateWalletRequest, UpdateWalletRequest, Wallet, WalletPage } from './wallets.models';

@Injectable({ providedIn: 'root' })
export class WalletsService {
  constructor(private readonly http: HttpClient) {}

  list(options: { includeArchived?: boolean } & CursorPageOptions = {}): Observable<WalletPage> {
    let params = new HttpParams();
    if (options.includeArchived) {
      params = params.set('includeArchived', 'true');
    }
    params = toCursorParams(options, params);

    return this.http.get<WalletPage>(`${environment.apiBaseUrl}/wallets`, { params });
  }

  create(request: CreateWalletRequest): Observable<Wallet> {
    return this.http.post<Wallet>(`${environment.apiBaseUrl}/wallets`, request);
  }

  /** PATCH /wallets/{id} (UC-02). 409 — см. docs/architecture/adr/0002 (не применимо здесь), Q8 (тихое игнорирование includeInTotal, не 409). */
  update(id: string, request: UpdateWalletRequest): Observable<Wallet> {
    return this.http.patch<Wallet>(`${environment.apiBaseUrl}/wallets/${id}`, request);
  }

  /** POST /wallets/{id}/archive (UC-03). 409 — основной кошелек нельзя архивировать (Q7). */
  archive(id: string): Observable<Wallet> {
    return this.http.post<Wallet>(`${environment.apiBaseUrl}/wallets/${id}/archive`, {});
  }

  /** POST /wallets/{id}/set-primary (UC-05). 409 — архивный кошелек нельзя сделать основным. */
  setPrimary(id: string): Observable<Wallet> {
    return this.http.post<Wallet>(`${environment.apiBaseUrl}/wallets/${id}/set-primary`, {});
  }

  /** PUT /wallets/{id}/currency (UC-06). 409 — у кошелька уже есть операции/слепки (Q15). */
  changeCurrency(id: string, request: ChangeWalletCurrencyRequest): Observable<Wallet> {
    return this.http.put<Wallet>(`${environment.apiBaseUrl}/wallets/${id}/currency`, request);
  }

  /** DELETE /wallets/{id} (UC-04). 409 — история операций/слепков ИЛИ кошелек основной (ADR-0002). */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/wallets/${id}`);
  }
}
