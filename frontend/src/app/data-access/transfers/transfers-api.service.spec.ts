import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { Transfer } from './transfers-api.models';
import { TransfersApiService } from './transfers-api.service';

const restored: Transfer = {
  id: 't-1',
  sourceWalletId: 'wallet-1',
  targetWalletId: 'wallet-2',
  sourceOperationId: 'op-1',
  targetOperationId: 'op-2',
  amount: { amount: '100.00', currencyId: 'c-usd' },
  transferDate: '2026-09-10',
  createdAt: '2026-09-10T00:00:00Z',
  isDeleted: false,
  deletedAt: null,
};

describe('TransfersApiService', () => {
  let service: TransfersApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(TransfersApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('restore sends POST /transfers/{id}/restore with an empty body and returns the restored transfer', () => {
    let result: Transfer | undefined;
    service.restore('t-1').subscribe((transfer) => (result = transfer));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/transfers/t-1/restore`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({});
    req.flush(restored);

    expect(result).toEqual(restored);
  });

  it('restore propagates a 409 error to the subscriber', () => {
    let status: number | undefined;
    service.restore('t-1').subscribe({ error: (error) => (status = error.status) });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers/t-1/restore`)
      .flush({ title: 'Conflict', status: 409 }, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
  });

  it('delete sends DELETE /transfers/{id}', () => {
    service.delete('t-1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/transfers/t-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
