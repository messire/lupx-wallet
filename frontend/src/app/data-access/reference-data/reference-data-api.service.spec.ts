import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { AvailableCurrency, Currency, CurrencyPage } from './reference-data-api.models';
import { ReferenceDataApiService } from './reference-data-api.service';

describe('ReferenceDataApiService (currencies, ADR-0004 §7)', () => {
  let service: ReferenceDataApiService;
  let httpMock: HttpTestingController;

  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [HttpClientTestingModule] });
    service = TestBed.inject(ReferenceDataApiService);
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('listAvailableCurrencies sends GET /currencies/available without query parameters and returns the plain array', () => {
    const available: AvailableCurrency[] = [
      { code: 'EUR', name: 'Euro', isActive: false },
      { code: 'USD', name: 'US Dollar', isActive: true },
    ];
    let result: AvailableCurrency[] | undefined;
    service.listAvailableCurrencies().subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.keys()).toEqual([]);
    req.flush(available);

    expect(result).toEqual(available);
  });

  it('listAvailableCurrencies propagates an error to the subscriber', () => {
    let status: number | undefined;
    service.listAvailableCurrencies().subscribe({ error: (error) => (status = error.status) });

    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(status).toBe(500);
  });

  it('createCurrency sends POST /currencies with only the code in the body', () => {
    const created: Currency = { id: 'c-1', code: 'EUR', name: 'Euro' };
    let result: Currency | undefined;
    service.createCurrency('EUR').subscribe((value) => (result = value));

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'EUR' });
    req.flush(created, { status: 201, statusText: 'Created' });

    expect(result).toEqual(created);
  });

  it('createCurrency propagates 400 (not available) and 409 (already active)', () => {
    const statuses: number[] = [];
    service.createCurrency('XXX').subscribe({ error: (error) => statuses.push(error.status) });
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush(null, { status: 400, statusText: 'Bad Request' });

    service.createCurrency('EUR').subscribe({ error: (error) => statuses.push(error.status) });
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush(null, { status: 409, statusText: 'Conflict' });

    expect(statuses).toEqual([400, 409]);
  });

  it('listCurrencies passes limit and never sends includeInactive', () => {
    const page: CurrencyPage = { data: [{ id: 'c-1', code: 'USD', name: 'US Dollar' }], pagination: { nextCursor: null, hasMore: false } };
    let result: CurrencyPage | undefined;
    service.listCurrencies({ limit: 100 }).subscribe((value) => (result = value));

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`);
    expect(req.request.method).toBe('GET');
    expect(req.request.params.get('limit')).toBe('100');
    expect(req.request.params.has('includeInactive')).toBe(false);
    req.flush(page);

    expect(result).toEqual(page);
  });

  it('listCurrencies without options sends no query parameters at all', () => {
    service.listCurrencies().subscribe();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`);
    expect(req.request.params.keys()).toEqual([]);
    req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
  });

  it('listCurrencies ignores a stray includeInactive option (the API has no inactive currencies)', () => {
    service.listCurrencies({ includeInactive: true, limit: 50 } as unknown as { limit: number }).subscribe();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`);
    expect(req.request.params.has('includeInactive')).toBe(false);
    expect(req.request.params.get('limit')).toBe('50');
    req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
  });

  it('deleteCurrency sends DELETE /currencies/{id} ("remove from active")', () => {
    let completed = false;
    service.deleteCurrency('c-1').subscribe({ complete: () => (completed = true) });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    expect(completed).toBe(true);
  });

  it('deleteCurrency propagates a 409 (currency in use) to the subscriber', () => {
    let status: number | undefined;
    service.deleteCurrency('c-1').subscribe({ error: (error) => (status = error.status) });

    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`).flush(null, { status: 409, statusText: 'Conflict' });

    expect(status).toBe(409);
  });

  it('has no currency deactivation call (ADR-0004 §7: a currency is either active or not)', () => {
    expect('deactivateCurrency' in service).toBe(false);
  });
});
