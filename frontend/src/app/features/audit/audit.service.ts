import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable } from '@angular/core';
import { Observable } from 'rxjs';
import { environment } from '../../../environments/environment';
import { CursorPageOptions, toCursorParams } from '../../core/api/cursor-page';
import { AuditEntryPage } from './audit.models';

/**
 * GET /audit-entries (UC-25, docs/api/openapi.yaml) — курсорная пагинация,
 * сортировка occurred_at DESC. Единственная точка HTTP-доступа к аудиту:
 * переиспользуется самостоятельным экраном `/audit` и ссылками "История
 * изменений" из features/wallets и features/operations (docs/PROGRESS.md, W3.2).
 */
@Injectable({ providedIn: 'root' })
export class AuditService {
  constructor(private readonly http: HttpClient) {}

  list(entityType: string, entityId: string, options: CursorPageOptions = {}): Observable<AuditEntryPage> {
    let params = new HttpParams().set('entityType', entityType).set('entityId', entityId);
    params = toCursorParams(options, params);

    return this.http.get<AuditEntryPage>(`${environment.apiBaseUrl}/audit-entries`, { params });
  }
}
