import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ExchangeRatesComponent } from './exchange-rates-page.component';

const currenciesResponse = {
  data: [
    { id: 'cur-usd', code: 'USD', name: 'US Dollar', isActive: true },
    { id: 'cur-eur', code: 'EUR', name: 'Euro', isActive: true },
  ],
  pagination: { nextCursor: null, hasMore: false },
};

describe('ExchangeRatesComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ExchangeRatesComponent, HttpClientTestingModule],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent() {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({
      lastSuccessfulUpdate: '2026-09-10T12:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9123' }],
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);

    fixture.detectChanges();
    return fixture;
  }

  it('loads latest rates and resolves currency codes via ReferenceDataApiService', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
    expect(component.isLoading()).toBe(false);
  });

  it('shows "никогда" when the source has never successfully updated (UC-10)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    fixture.detectChanges();

    expect(fixture.componentInstance.lastUpdateLabel()).toBe('никогда');
  });

  it('formats the last successful update timestamp when present (UC-10)', () => {
    const fixture = createComponent();
    expect(fixture.componentInstance.lastUpdateLabel()).not.toBe('никогда');
  });

  it('refreshes rates on demand (UC-09)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();
    expect(component.isRefreshing()).toBe(true);

    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
    expect(refreshReq.request.method).toBe('POST');
    refreshReq.flush({
      lastSuccessfulUpdate: '2026-09-11T08:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9200' }],
    });

    expect(component.isRefreshing()).toBe(false);
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9200' }]);
  });

  it('treats a 502 from the refresh endpoint as a friendly warning, not an application error (UC-09)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();

    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
    refreshReq.flush(
      { lastSuccessfulUpdate: '2026-09-10T12:00:00Z', rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9123' }] },
      { status: 502, statusText: 'Bad Gateway' },
    );

    expect(component.isRefreshing()).toBe(false);
    expect(component.loadError()).toBeNull();
    expect(component.sourceUnavailable()).toBe('Источник курсов недоступен, показаны последние известные курсы.');
    // Previous rates are kept (ADR-0001 §6), not cleared or flagged as an error.
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
  });
});
