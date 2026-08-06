import { HttpClient } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { LatestExchangeRates } from './exchange-rates.models';

/**
 * HTTP-доступ к докс/api/openapi.yaml: /exchange-rates/latest, /exchange-rates/refresh
 * (UC-08, UC-09, UC-10, docs/PROGRESS.md W2.4). Обе операции возвращают одну и ту же
 * схему LatestExchangeRates — в том числе `refresh` при 502 (источник Frankfurter
 * недоступен, ADR-0001 п.6: возвращаются последние известные курсы, а не ошибка без тела).
 */
@Injectable({ providedIn: 'root' })
export class ExchangeRatesService {
  constructor(private readonly http: HttpClient) {}

  getLatest(): Observable<LatestExchangeRates> {
    return this.http.get<LatestExchangeRates>(`${environment.apiBaseUrl}/exchange-rates/latest`);
  }

  refresh(): Observable<LatestExchangeRates> {
    return this.http.post<LatestExchangeRates>(`${environment.apiBaseUrl}/exchange-rates/refresh`, {});
  }
}
