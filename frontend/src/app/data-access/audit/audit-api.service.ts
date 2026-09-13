import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../shared/pagination/cursor-page';
import { AuditEntryPage } from './audit-api.models';

/**
 * GET /audit-entries (UC-25, docs/api/openapi.yaml) — cursor-based pagination,
 * sorted by occurred_at DESC. Single HTTP access point for audit data,
 * reused by the standalone `/audit` screen and the "change history" links
 * in features/wallets and features/operations.
 */
@Injectable({ providedIn: 'root' })
export class AuditApiService {
  constructor(private readonly http: HttpClient) {}

  list(entityType: string, entityId: string, options: CursorPageOptions = {}): Observable<AuditEntryPage> {
    let params = new HttpParams().set('entityType', entityType).set('entityId', entityId);
    params = toCursorParams(options, params);

    return this.http.get<AuditEntryPage>(`${environment.apiBaseUrl}/audit-entries`, { params });
  }
}
