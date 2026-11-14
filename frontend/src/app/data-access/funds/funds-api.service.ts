import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import { Fund, FundAllocationRequest, FundPage, FundWriteRequest, WalletReservation } from './funds-api.models';

/** HTTP client of the Funds module (docs/api/openapi.yaml, tag Funds; ADR-0010 §4). */
@Injectable({ providedIn: 'root' })
export class FundsApiService {
  private readonly baseUrl = `${environment.apiBaseUrl}/funds`;

  constructor(private readonly http: HttpClient) {}

  /** Closed funds are listed only with `includeClosed`. */
  list(options: { includeClosed?: boolean } & CursorPageOptions = {}): Observable<FundPage> {
    let params = toCursorParams(options);
    if (options.includeClosed) {
      params = params.set('includeClosed', 'true');
    }
    return this.http.get<FundPage>(this.baseUrl, { params });
  }

  get(id: string): Observable<Fund> {
    return this.http.get<Fund>(`${this.baseUrl}/${id}`);
  }

  create(request: FundWriteRequest): Observable<Fund> {
    return this.http.post<Fund>(this.baseUrl, request);
  }

  /** All fields are sent as a whole (an omitted debt/loan link is removed). */
  update(id: string, request: FundWriteRequest): Observable<Fund> {
    return this.http.patch<Fund>(`${this.baseUrl}/${id}`, request);
  }

  /** Only a fund without reserve history (409 otherwise — close it instead). */
  delete(id: string): Observable<void> {
    return this.http.delete<void>(`${this.baseUrl}/${id}`);
  }

  /** Releases every reserve; final. */
  close(id: string): Observable<Fund> {
    return this.http.post<Fund>(`${this.baseUrl}/${id}/close`, {});
  }

  /** Not more than the wallet's free funds (409 `fund-insufficient-free-funds` with the available amount in `detail`). */
  reserve(id: string, request: FundAllocationRequest): Observable<Fund> {
    return this.http.post<Fund>(`${this.baseUrl}/${id}/reserve`, request);
  }

  release(id: string, request: FundAllocationRequest): Observable<Fund> {
    return this.http.post<Fund>(`${this.baseUrl}/${id}/release`, request);
  }

  /** Every wallet, archived ones included; no pagination. */
  listWalletReservations(): Observable<WalletReservation[]> {
    return this.http.get<WalletReservation[]>(`${this.baseUrl}/wallet-reservations`);
  }
}
