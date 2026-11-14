import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { DebtsApiService } from './debts-api.service';

const base = environment.apiBaseUrl;

describe('DebtsApiService', () => {
  let service: DebtsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(DebtsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listCreditors sends includeInactive only when requested', () => {
    service.listCreditors().subscribe();
    const plain = httpMock.expectOne((req) => req.url === `${base}/creditors`);
    expect(plain.request.params.has('includeInactive')).toBe(false);
    plain.flush({ items: [], nextCursor: null });

    service.listCreditors({ includeInactive: true }).subscribe();
    const withInactive = httpMock.expectOne((req) => req.url === `${base}/creditors`);
    expect(withInactive.request.params.get('includeInactive')).toBe('true');
    withInactive.flush({ items: [], nextCursor: null });
  });

  it('listDebts passes the creditor filter as a query parameter', () => {
    service.listDebts({ creditorId: 'cr-1' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/debts`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('creditorId')).toBe('cr-1');
    req.flush({ items: [], nextCursor: null });
  });

  it('recordRepayment posts the payment to /debts/{id}/repayments', () => {
    const body = { walletId: 'w-1', operationTypeId: 'ot-1', amount: '100.00', date: '2026-11-10' };
    service.recordRepayment('d-1', body as never).subscribe();
    const req = httpMock.expectOne(`${base}/debts/d-1/repayments`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });

  it('cancelDebt posts an empty body to /debts/{id}/cancel', () => {
    service.cancelDebt('d-1').subscribe();
    const req = httpMock.expectOne(`${base}/debts/d-1/cancel`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush({});
  });

  it('replaceSchedule sends PUT and generateSchedule sends POST to the loan schedule', () => {
    service.replaceSchedule('l-1', { items: [] } as never).subscribe();
    const put = httpMock.expectOne(`${base}/loans/l-1/schedule`);
    expect(put.request.method).toBe('PUT');
    put.flush({});

    const generate = { firstPaymentDate: '2026-12-01', count: 12 };
    service.generateSchedule('l-1', generate as never).subscribe();
    const post = httpMock.expectOne(`${base}/loans/l-1/schedule/generate`);
    expect(post.request.method).toBe('POST');
    expect(post.request.body).toEqual(generate);
    post.flush({});
  });

  it('recordLoanPayment posts to /loans/{id}/payments', () => {
    service.recordLoanPayment('l-1', { amount: '1.00' } as never).subscribe();
    const req = httpMock.expectOne(`${base}/loans/l-1/payments`);
    expect(req.request.method).toBe('POST');
    req.flush({});
  });

  it('deleteCreditor propagates 409 (creditor in use)', () => {
    let status: number | undefined;
    service.deleteCreditor('cr-1').subscribe({ error: (error) => (status = error.status) });
    httpMock
      .expectOne(`${base}/creditors/cr-1`)
      .flush({ title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });
    expect(status).toBe(409);
  });
});
