import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { FundsApiService } from './funds-api.service';

const base = `${environment.apiBaseUrl}/funds`;

describe('FundsApiService', () => {
  let service: FundsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(FundsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list adds includeClosed only when requested', () => {
    service.list().subscribe();
    const plain = httpMock.expectOne((req) => req.url === base);
    expect(plain.request.params.has('includeClosed')).toBe(false);
    plain.flush({ items: [], nextCursor: null });

    service.list({ includeClosed: true }).subscribe();
    const closed = httpMock.expectOne((req) => req.url === base);
    expect(closed.request.params.get('includeClosed')).toBe('true');
    closed.flush({ items: [], nextCursor: null });
  });

  it('reserve and release post the allocation to their endpoints', () => {
    const body = { walletId: 'w-1', amount: '50.00' };
    service.reserve('f-1', body as never).subscribe();
    const reserve = httpMock.expectOne(`${base}/f-1/reserve`);
    expect(reserve.request.method).toBe('POST');
    expect(reserve.request.body).toEqual(body);
    reserve.flush({});

    service.release('f-1', body as never).subscribe();
    const release = httpMock.expectOne(`${base}/f-1/release`);
    expect(release.request.method).toBe('POST');
    release.flush({});
  });

  it('reserve propagates 409 fund-insufficient-free-funds', () => {
    let status: number | undefined;
    service.reserve('f-1', { walletId: 'w-1', amount: '999.00' } as never).subscribe({
      error: (error) => (status = error.status),
    });
    httpMock
      .expectOne(`${base}/f-1/reserve`)
      .flush({ type: 'fund-insufficient-free-funds', status: 409 }, { status: 409, statusText: 'Conflict' });
    expect(status).toBe(409);
  });

  it('update sends PATCH, close sends POST with an empty body', () => {
    service.update('f-1', { name: 'Отпуск' } as never).subscribe();
    const patch = httpMock.expectOne(`${base}/f-1`);
    expect(patch.request.method).toBe('PATCH');
    patch.flush({});

    service.close('f-1').subscribe();
    const close = httpMock.expectOne(`${base}/f-1/close`);
    expect(close.request.method).toBe('POST');
    expect(close.request.body).toEqual({});
    close.flush({});
  });

  it('listWalletReservations reads /funds/wallet-reservations', () => {
    let result: unknown;
    service.listWalletReservations().subscribe((items) => (result = items));
    const req = httpMock.expectOne(`${base}/wallet-reservations`);
    expect(req.request.method).toBe('GET');
    req.flush([]);
    expect(result).toEqual([]);
  });
});
