import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { BalanceHistoryService } from './balance-history.service';

describe('BalanceHistoryService', () => {
  let service: BalanceHistoryService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(BalanceHistoryService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('requests balance without a date parameter when none is provided', () => {
    service.getBalanceOnDate('wallet-1').subscribe();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance`);
    expect(req.request.params.has('date')).toBe(false);
    req.flush({ walletId: 'wallet-1', date: '2026-09-01', balance: { amount: '10.00', currencyId: 'c-1' } });
  });

  it('requests balance with a date parameter when provided', () => {
    service.getBalanceOnDate('wallet-1', '2026-06-01').subscribe();

    const req = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-06-01',
    );
    req.flush({ walletId: 'wallet-1', date: '2026-06-01', balance: { amount: '10.00', currencyId: 'c-1' } });
  });

  it('requests history with from/to and optional cursor', () => {
    service.listHistory('wallet-1', '2026-01-01', '2026-01-31', { cursor: 'abc', limit: 20 }).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`);
    expect(req.request.params.get('from')).toBe('2026-01-01');
    expect(req.request.params.get('to')).toBe('2026-01-31');
    expect(req.request.params.get('cursor')).toBe('abc');
    expect(req.request.params.get('limit')).toBe('20');
    req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
  });
});
