import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ReportingApiService } from './reporting-api.service';

const base = `${environment.apiBaseUrl}/reporting`;

describe('ReportingApiService', () => {
  let service: ReportingApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ReportingApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('getTotalAmount sends date only for a historical total', () => {
    service.getTotalAmount().subscribe();
    const current = httpMock.expectOne((req) => req.url === `${base}/total-amount`);
    expect(current.request.params.has('date')).toBe(false);
    current.flush({});

    service.getTotalAmount('2026-05-01').subscribe();
    const historical = httpMock.expectOne((req) => req.url === `${base}/total-amount`);
    expect(historical.request.params.get('date')).toBe('2026-05-01');
    historical.flush({});
  });

  it('getFreeFunds and getDebtsSummary read their reports', () => {
    service.getFreeFunds().subscribe();
    expect(httpMock.expectOne(`${base}/free-funds`).request.method).toBe('GET');

    service.getDebtsSummary().subscribe();
    expect(httpMock.expectOne(`${base}/debts-summary`).request.method).toBe('GET');

    httpMock.match(() => true).forEach((req) => req.flush({}));
  });

  it('getUpcomingPayments passes days (30 by default)', () => {
    service.getUpcomingPayments().subscribe();
    const byDefault = httpMock.expectOne((req) => req.url === `${base}/upcoming-payments`);
    expect(byDefault.request.params.get('days')).toBe('30');
    byDefault.flush({});

    service.getUpcomingPayments(7).subscribe();
    expect(httpMock.expectOne((req) => req.url === `${base}/upcoming-payments`).request.params.get('days')).toBe('7');
    httpMock.match(() => true).forEach((req) => req.flush({}));
  });

  it('getCashFlowForecast passes the horizon in months', () => {
    service.getCashFlowForecast(12).subscribe();
    const req = httpMock.expectOne((r) => r.url === `${base}/cash-flow-forecast`);
    expect(req.request.params.get('months')).toBe('12');
    req.flush({});
  });

  it('propagates 404 when there is no primary wallet', () => {
    let status: number | undefined;
    service.getFreeFunds().subscribe({ error: (error) => (status = error.status) });
    httpMock.expectOne(`${base}/free-funds`).flush(null, { status: 404, statusText: 'Not Found' });
    expect(status).toBe(404);
  });
});
