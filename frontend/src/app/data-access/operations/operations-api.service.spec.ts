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

  it('delete sends DELETE /operations/{id}', () => {
    service.delete('op-1').subscribe();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/op-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });
  });
});
