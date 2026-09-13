import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { TotalAmount } from './reporting-api.models';

/**
 * GET /reporting/total-amount — without `date` returns the current total (UC-19),
 * with `date` returns a historical one (UC-21). The only endpoint of the Reporting module.
 */
@Injectable({ providedIn: 'root' })
export class ReportingApiService {
  constructor(private readonly http: HttpClient) {}

  getTotalAmount(date?: string): Observable<TotalAmount> {
    let params = new HttpParams();
    if (date) {
      params = params.set('date', date);
    }
    return this.http.get<TotalAmount>(`${environment.apiBaseUrl}/reporting/total-amount`, { params });
  }
}
