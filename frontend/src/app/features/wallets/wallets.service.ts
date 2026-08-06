import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CreateWalletRequest, Wallet, WalletPage } from './wallets.models';

@Injectable({ providedIn: 'root' })
export class WalletsService {
  constructor(private readonly http: HttpClient) {}

  list(options: { includeArchived?: boolean; cursor?: string | null; limit?: number } = {}): Observable<WalletPage> {
    let params = new HttpParams();
    if (options.includeArchived) {
      params = params.set('includeArchived', 'true');
    }
    if (options.cursor) {
      params = params.set('cursor', options.cursor);
    }
    if (options.limit) {
      params = params.set('limit', options.limit);
    }

    return this.http.get<WalletPage>(`${environment.apiBaseUrl}/wallets`, { params });
  }

  create(request: CreateWalletRequest): Observable<Wallet> {
    return this.http.post<Wallet>(`${environment.apiBaseUrl}/wallets`, request);
  }
}
