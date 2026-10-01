import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { Operation } from './operations-api.models';
import { OperationsApiService } from './operations-api.service';

const restored: Operation = {
  id: 'op-1',
  walletId: 'wallet-1',
  operationTypeId: 'ot-1',
  amount: { amount: '10.00', currencyId: 'c-usd' },
  operationDate: '2026-09-10',
  adjustmentMode: null,
  isCorrection: false,
  statedBalance: null,
  transferId: null,
  isDeleted: false,
  deletedAt: null,
  createdAt: '2026-09-10T00:00:00Z',
  updatedAt: '2026-09-10T00:00:00Z',
};

describe('OperationsApiService', () => {
  let service: OperationsApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(OperationsApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('restore sends POST /operations/{id}/restore with an empty body and returns the restored operation', () => {
    let result: Operation | undefined;
    service.restore('op-1').subscribe((operation) => (result = operation));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/op-1/restore`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush(restored);

    expect(result).toEqual(restored);
  });

  it('restore propagates a 409 error to the subscriber', () => {
    let status: number | undefined;
    service.restore('op-1').subscribe({ error: (error) => (status = error.status) });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/op-1/restore`)
      .flush({ title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
  });

  const correction: Operation = {
    ...restored,
    id: 'op-corr',
    operationTypeId: null,
    amount: { amount: '50.00', currencyId: 'c-usd' },
    isCorrection: true,
    statedBalance: { amount: '350.00', currencyId: 'c-usd' },
  };

  it('setStatedBalance sends PUT /wallets/{id}/stated-balances/{date} with the amount and reports 201 as created', () => {
    let result: { correction: Operation; created: boolean } | undefined;
    service.setStatedBalance('wallet-1', '2026-09-10', '350.00').subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2026-09-10`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ amount: '350.00' });
    req.flush(correction, { status: 201, statusText: 'Created' });

    expect(result).toEqual({ correction, created: true });
  });

  it('setStatedBalance reports 200 as a replacement of the previous value', () => {
    let result: { correction: Operation; created: boolean } | undefined;
    service.setStatedBalance('wallet-1', '2026-09-10', '-5').subscribe((value) => (result = value));

    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2026-09-10`)
      .flush(correction, { status: 200, statusText: 'OK' });

    expect(result?.created).toBe(false);
    expect(result?.correction.isCorrection).toBe(true);
  });

  it('setStatedBalance propagates a 400 error to the subscriber', () => {
    let status: number | undefined;
    service.setStatedBalance('wallet-1', '2030-01-01', '1').subscribe({ error: (error) => (status = error.status) });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2030-01-01`)
      .flush({ title: 'Bad Request', status: 400 }, { status: 400, statusText: 'Bad Request' });

    expect(status).toBe(400);
  });

  it('delete sends DELETE /operations/{id}', () => {
    service.delete('op-1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/op-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
