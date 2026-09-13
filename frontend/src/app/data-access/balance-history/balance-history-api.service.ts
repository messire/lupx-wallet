import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import { BalanceSnapshot, BalanceSnapshotPage } from './balance-history-api.models';

/**
 * GET /wallets/{id}/balance (UC-18/UC-20 — баланс на дату, без параметра date —
 * текущий баланс) и GET /wallets/{id}/balance-history (курсорная пагинация,
 * UC-23 отражается здесь просто как уже пересчитанный бэкендом результат).
 */
@Injectable({ providedIn: 'root' })
export class BalanceHistoryApiService {
  constructor(private readonly http: HttpClient) {}

  getBalanceOnDate(walletId: string, date?: string): Observable<BalanceSnapshot> {
    let params = new HttpParams();
    if (date) {
      params = params.set('date', date);
    }
    return this.http.get<BalanceSnapshot>(`${environment.apiBaseUrl}/wallets/${walletId}/balance`, { params });
  }

  listHistory(walletId: string, from: string, to: string, options: CursorPageOptions = {}): Observable<BalanceSnapshotPage> {
    let params = new HttpParams().set('from', from).set('to', to);
    params = toCursorParams(options, params);

    return this.http.get<BalanceSnapshotPage>(`${environment.apiBaseUrl}/wallets/${walletId}/balance-history`, { params });
  }
}
