import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LatestExchangeRates } from './exchange-rates-api.models';

/**
 * HTTP access to docs/api/openapi.yaml: /exchange-rates/latest, /exchange-rates/refresh
 * (UC-08, UC-09, UC-10). Both operations return the same LatestExchangeRates schema —
 * including `refresh` on a 502 (Frankfurter source unavailable, ADR-0001 §6: the last
 * known rates are returned instead of a bodyless error).
 */
@Injectable({ providedIn: 'root' })
export class ExchangeRatesApiService {
  constructor(private readonly http: HttpClient) {}

  getLatest(): Observable<LatestExchangeRates> {
    return this.http.get<LatestExchangeRates>(`${environment.apiBaseUrl}/exchange-rates/latest`);
  }

  refresh(): Observable<LatestExchangeRates> {
    return this.http.post<LatestExchangeRates>(`${environment.apiBaseUrl}/exchange-rates/refresh`, {});
  }
}
