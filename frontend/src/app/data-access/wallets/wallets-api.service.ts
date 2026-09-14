import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import { ChangeWalletCurrencyRequest, CreateWalletRequest, UpdateWalletRequest, Wallet, WalletPage } from './wallets-api.models';

@Injectable({ providedIn: 'root' })
export class WalletsApiService {
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

  /** PATCH /wallets/{id} (UC-02). Q8: includeInTotal is silently ignored for the primary wallet, not a 409. */
  update(id: string, request: UpdateWalletRequest): Observable<Wallet> {
    return this.http.patch<Wallet>(`${environment.apiBaseUrl}/wallets/${id}`, request);
  }

  /** POST /wallets/{id}/archive (UC-03). 409 — the primary wallet cannot be archived (Q7). */
  archive(id: string): Observable<Wallet> {
    return this.http.post<Wallet>(`${environment.apiBaseUrl}/wallets/${id}/archive`, {});
  }

  /** POST /wallets/{id}/set-primary (UC-05). 409 — an archived wallet cannot be made primary. */
  setPrimary(id: string): Observable<Wallet> {
    return this.http.post<Wallet>(`${environment.apiBaseUrl}/wallets/${id}/set-primary`, {});
  }

  /** PUT /wallets/{id}/currency (UC-06). 409 — the wallet already has operations/snapshots (Q15). */
  changeCurrency(id: string, request: ChangeWalletCurrencyRequest): Observable<Wallet> {
    return this.http.put<Wallet>(`${environment.apiBaseUrl}/wallets/${id}/currency`, request);
  }

  /** DELETE /wallets/{id} (UC-04). 409 — operation/snapshot history OR the wallet is primary (ADR-0002). */
  remove(id: string): Observable<void> {
    return this.http.delete<void>(`${environment.apiBaseUrl}/wallets/${id}`);
  }
}
