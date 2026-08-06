import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AuditService } from './audit.service';

describe('AuditService', () => {
  let service: AuditService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(AuditService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests audit entries with entityType/entityId', () => {
    service.list('Wallet', 'wallet-1').subscribe();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/audit-entries`);
    expect(req.request.params.get('entityType')).toBe('Wallet');
    expect(req.request.params.get('entityId')).toBe('wallet-1');
    req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
  });

  it('adds cursor/limit when provided', () => {
    service.list('Operation', 'operation-1', { cursor: 'abc', limit: 20 }).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/audit-entries`);
    expect(req.request.params.get('cursor')).toBe('abc');
    expect(req.request.params.get('limit')).toBe('20');
    req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
  });
});
