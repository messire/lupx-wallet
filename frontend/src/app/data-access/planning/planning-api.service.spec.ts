import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { PlanningApiService } from './planning-api.service';

const base = environment.apiBaseUrl;
const occurrence = `${base}/planned-transactions/p-1/occurrences/2026-11-10`;

describe('PlanningApiService', () => {
  let service: PlanningApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(PlanningApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('list passes the status filter', () => {
    service.list({ status: 'Active' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/planned-transactions`);
    expect(req.request.params.get('status')).toBe('Active');
    req.flush({ items: [], nextCursor: null });
  });

  it('listOccurrences sends the period and the optional plan filter', () => {
    service.listOccurrences({ from: '2026-11-01', to: '2026-11-30', plannedTransactionId: 'p-1' }).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/planned-occurrences`);
    expect(req.request.params.get('from')).toBe('2026-11-01');
    expect(req.request.params.get('to')).toBe('2026-11-30');
    expect(req.request.params.get('plannedTransactionId')).toBe('p-1');
    req.flush({ items: [] });
  });

  it('delete returns null on 204 and the finished plan on 200', () => {
    let first: unknown = 'unset';
    service.delete('p-1').subscribe((result) => (first = result));
    httpMock.expectOne(`${base}/planned-transactions/p-1`).flush(null, { status: 204, statusText: 'No Content' });
    expect(first).toBeNull();

    let second: unknown;
    service.delete('p-1').subscribe((result) => (second = result));
    httpMock.expectOne(`${base}/planned-transactions/p-1`).flush({ id: 'p-1', status: 'Finished' });
    expect(second).toEqual({ id: 'p-1', status: 'Finished' });
  });

  it('occurrence actions hit the occurrence sub-resources', () => {
    service.skipOccurrence('p-1', '2026-11-10').subscribe();
    expect(httpMock.expectOne(`${occurrence}/skip`).request.method).toBe('POST');

    service.unskipOccurrence('p-1', '2026-11-10').subscribe();
    expect(httpMock.expectOne(`${occurrence}/unskip`).request.method).toBe('POST');

    service.moveOccurrence('p-1', '2026-11-10', '2026-11-12').subscribe();
    const move = httpMock.expectOne(`${occurrence}/move`);
    expect(move.request.body).toEqual({ date: '2026-11-12' });

    service.changeOccurrenceAmount('p-1', '2026-11-10', null).subscribe();
    const amount = httpMock.expectOne(`${occurrence}/amount`);
    expect(amount.request.method).toBe('PUT');
    expect(amount.request.body).toEqual({ amount: null });

    httpMock.match(() => true).forEach((req) => req.flush({}));
  });

  it('executeOccurrence posts the prefilled operation data', () => {
    const body = { walletId: 'w-1', amount: '100.00', date: '2026-11-10' };
    service.executeOccurrence('p-1', '2026-11-10', body as never).subscribe();
    const req = httpMock.expectOne(`${occurrence}/execute`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual(body);
    req.flush({});
  });
});
