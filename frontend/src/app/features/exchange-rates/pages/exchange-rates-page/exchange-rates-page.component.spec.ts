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

// One primary wallet in EUR (ADR-0004 §5: quotes are relative to the primary
// wallet's currency) — resolved via GET /wallets, not guessed from a row.
const walletsResponse = {
  data: [
    {
      id: 'wallet-eur',
      name: 'Основной кошелек',
      walletTypeId: 'wt-1',
      purposeDescription: null,
      currencyId: 'cur-eur',
      initialBalance: { amount: '0', currencyId: 'cur-eur' },
      accountingStartDate: '2026-01-01',
      currentBalance: { amount: '0', currencyId: 'cur-eur' },
      includeInTotal: true,
      isPrimary: true,
      isArchived: false,
      displayOrder: 0,
      color: null,
      icon: null,
      createdAt: '2026-01-01T00:00:00Z',
      updatedAt: '2026-01-01T00:00:00Z',
    },
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
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);

    fixture.detectChanges();
    return fixture;
  }

  it('loads latest rates and resolves currency codes via ReferenceDataApiService', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
    expect(component.isLoading()).toBe(false);
  });

  it('shows "Ещё не было" when the source has never successfully updated (UC-10)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    fixture.detectChanges();

    expect(fixture.componentInstance.lastUpdateLabel()).toBe('Ещё не было');
  });

  it('shows "—" while the primary load is in flight (UC-10)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    expect(fixture.componentInstance.lastUpdateLabel()).toBe('—');

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
  });

  it('formats the last successful update timestamp when present (UC-10)', () => {
    const fixture = createComponent();
    expect(fixture.componentInstance.lastUpdateLabel()).not.toBe('Ещё не было');
  });

  it('resolves the primary currency from the primary wallet, not from a row (C3)', () => {
    const fixture = createComponent();
    expect(fixture.componentInstance.primaryCurrencyCode()).toBe('EUR');
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
    expect(component.successMessage()).toBe('Курсы валют обновлены.');
  });

  it('treats a 502 from the refresh endpoint as a friendly warning, not an application error, when previous data exists (UC-09)', () => {
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
    expect(component.sourceUnavailable()).toBe('Источник курсов недоступен сейчас. Показаны последние известные курсы.');
    expect(component.sourceUnavailableEmpty()).toBeNull();
    // Previous rates are kept (ADR-0004 §6), not cleared or flagged as an error.
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
  });

  it('shows a dedicated empty state (not fabricated rows) when a 502 hits with no previous data at all (UC-09, П.1 №1)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.refresh();

    // Per openapi.yaml, a 502 from this endpoint always carries a LatestExchangeRates
    // body (never a bare/empty error) — here it reflects that nothing has ever succeeded.
    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
    refreshReq.flush({ lastSuccessfulUpdate: null, rates: [] }, { status: 502, statusText: 'Bad Gateway' });

    expect(component.isRefreshing()).toBe(false);
    expect(component.sourceUnavailable()).toBeNull();
    expect(component.sourceUnavailableEmpty()).toBeTruthy();
    expect(component.rows()).toEqual([]);
    expect(component.lastSuccessfulUpdate()).toBeNull();
  });

  it('resolves the primary currency from the primary wallet even when the rates list is empty, not guessed from a row (C3)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    // No rows at all — if the code guessed the primary currency from a row's
    // toCurrencyId (the old approach), this would be null. It must come from
    // the primary wallet instead, regardless of the (empty) rates list.
    expect(component.rows()).toEqual([]);
    expect(component.primaryCurrencyCode()).toBe('EUR');
  });

  it('shows a polite empty state without a currency code when no wallet is primary (primaryWalletMissing)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    const walletsWithoutPrimary = {
      data: [{ ...walletsResponse.data[0], isPrimary: false }],
      pagination: { nextCursor: null, hasMore: false },
    };

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({
      lastSuccessfulUpdate: '2026-09-10T12:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9123' }],
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsWithoutPrimary);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.primaryWalletMissing()).toBe(true);
    expect(component.primaryCurrencyCode()).toBeNull();
    expect(component.loadError()).toBeNull();
    expect(fixture.nativeElement.textContent).toContain('Основной кошелёк не назначен');
  });

  it('keeps existing rows visible and does not re-show the skeleton while a refresh is in flight (C5)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();
    fixture.detectChanges();

    expect(component.isRefreshing()).toBe(true);
    expect(component.isLoading()).toBe(false);
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
    expect(fixture.nativeElement.querySelector('app-skeleton')).toBeNull();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush({
      lastSuccessfulUpdate: '2026-09-11T08:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9200' }],
    });
  });

  it('does not send a second refresh request when "Обновить сейчас" is clicked again while one is already in flight', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();
    component.refresh();

    // httpMock.expectOne throws if more than one request matched — this proves
    // the guard (`if (this.isRefreshing()) return;`) actually stopped the second call.
    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
    refreshReq.flush({
      lastSuccessfulUpdate: '2026-09-11T08:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9200' }],
    });

    expect(component.isRefreshing()).toBe(false);
  });

  it('shows the new update time and a confirmation toast after a successful refresh (UC-09)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;
    const labelBefore = component.lastUpdateLabel();

    component.refresh();
    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush({
      lastSuccessfulUpdate: '2026-09-11T08:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9200' }],
    });
    fixture.detectChanges();

    expect(component.lastUpdateLabel()).not.toBe(labelBefore);
    expect(component.successMessage()).toBe('Курсы валют обновлены.');
    expect(fixture.nativeElement.querySelector('app-toast')).not.toBeNull();
  });

  it('does not show the "source unavailable" status on a normal page load, however stale lastSuccessfulUpdate is (П.1 №1)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({
      lastSuccessfulUpdate: '2020-01-01T00:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9123' }],
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.sourceUnavailable()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Источник недоступен');
  });

  it('keeps table rows visible and shows a refresh-error banner (not the full-page error state) when refresh fails with 500 (MAJOR-3)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();

    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
    refreshReq.flush('Internal error', { status: 500, statusText: 'Internal Server Error' });
    fixture.detectChanges();

    expect(component.isRefreshing()).toBe(false);
    // The primary-load error signal must stay untouched — it drives the
    // full-page app-error-state that would otherwise replace the table.
    expect(component.loadError()).toBeNull();
    expect(component.refreshError()).toBeTruthy();
    // Rows already on screen before the failed refresh are preserved.
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
    expect(fixture.nativeElement.querySelector('app-error-state')).toBeNull();
    expect(fixture.nativeElement.querySelector('table')).not.toBeNull();
  });

  it('reports a refresh-error banner (not the full-page error state) when refresh fails with a network error (status 0)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();

    const refreshReq = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
    refreshReq.error(new ProgressEvent('error'), { status: 0, statusText: 'Unknown Error' });
    fixture.detectChanges();

    expect(component.loadError()).toBeNull();
    expect(component.refreshError()).toBeTruthy();
    expect(fixture.nativeElement.querySelector('app-error-state')).toBeNull();
  });

  it('clears a previous refresh error once a later refresh attempt succeeds', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.refresh();
    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush('boom', { status: 500, statusText: 'Internal Server Error' });
    expect(component.refreshError()).toBeTruthy();

    component.refresh();
    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush({
      lastSuccessfulUpdate: '2026-09-11T08:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9200' }],
    });

    expect(component.refreshError()).toBeNull();
  });

  it('uses the quote\'s own toCurrencyId in each row, not the primary wallet code, when they differ (MAJOR-4, resync window)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    // Primary wallet is EUR (cur-eur), but the latest rates response still
    // carries quotes against the previous primary currency (cur-usd) — the
    // background recompute after a primary wallet switch has not caught up
    // yet (ADR-0004 §5).
    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({
      lastSuccessfulUpdate: '2026-09-10T12:00:00Z',
      rates: [{ fromCurrencyId: 'cur-eur', toCurrencyId: 'cur-usd', rate: '1.0962' }],
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.primaryCurrencyCode()).toBe('EUR');
    // The row must show USD (the quote's actual toCurrencyId), not EUR.
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'EUR', toCurrencyCode: 'USD', rate: '1.0962' }]);
  });

  it('displays a long decimal rate as-is, without rounding (ADR-0005)', () => {
    const fixture = TestBed.createComponent(ExchangeRatesComponent);
    fixture.detectChanges();

    httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({
      lastSuccessfulUpdate: '2026-09-10T12:00:00Z',
      rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.123456789' }],
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    fixture.detectChanges();

    expect(fixture.componentInstance.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.123456789' }]);
  });
});
