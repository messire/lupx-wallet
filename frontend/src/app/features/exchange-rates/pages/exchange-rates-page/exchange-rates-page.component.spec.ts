import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ExchangeRatesComponent } from './exchange-rates-page.component';

const currenciesResponse = {
  data: [
    { id: 'cur-usd', code: 'USD', name: 'US Dollar' },
    { id: 'cur-eur', code: 'EUR', name: 'Euro' },
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
    expect(component.sourceUnavailable()).toBe(
      'Курсы или список доступных валют обновить не удалось. Показаны последние известные курсы.',
    );
    expect(component.sourceUnavailableEmpty()).toBeNull();
    // Previous rates are kept (ADR-0004 §6), not cleared or flagged as an error.
    expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
  });

  it('shows a dedicated empty state (not fabricated rows) when a 502 hits with no previous data at all (UC-09)', () => {
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

  it('does not show the "source unavailable" status on a normal page load, however stale lastSuccessfulUpdate is', () => {
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

  // ---- ADR-0004 §7–§8: the list of available currencies, no primary wallet, active currencies only ----

  describe('without a primary wallet and with the available-currencies refresh', () => {
    const walletsWithoutPrimary = {
      data: [{ ...walletsResponse.data[0], isPrimary: false }],
      pagination: { nextCursor: null, hasMore: false },
    };
    const noWallets = { data: [], pagination: { nextCursor: null, hasMore: false } };

    function createWithoutPrimary(options: { wallets?: unknown; lastSuccessfulUpdate?: string | null } = {}) {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({
        lastSuccessfulUpdate: options.lastSuccessfulUpdate ?? null,
        rates: [],
      });
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(options.wallets ?? walletsWithoutPrimary);
      fixture.detectChanges();
      return fixture;
    }

    const refreshButton = (fixture: { nativeElement: unknown }) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('app-button button')).find(
        (b) => b.textContent?.trim() === 'Обновить сейчас',
      ) as HTMLButtonElement;

    it('loads active currencies with a limit and without includeInactive', () => {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();

      const currenciesReq = httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`);
      expect(currenciesReq.request.params.get('limit')).toBe('100');
      expect(currenciesReq.request.params.has('includeInactive')).toBe(false);
      // The page never asks for the available list on load — only a refresh does, on the server.
      httpMock.expectNone(`${environment.apiBaseUrl}/currencies/available`);

      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
      currenciesReq.flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    });

    it('shows the "no primary wallet" empty state, mentioning that refresh still loads the available currencies', () => {
      const fixture = createWithoutPrimary();
      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';

      expect(fixture.componentInstance.primaryWalletMissing()).toBe(true);
      expect(text).toContain('Основной кошелёк не назначен');
      expect(text).toContain('Кнопка «Обновить сейчас» и без него загружает список доступных валют.');
      expect((fixture.nativeElement as HTMLElement).querySelector('table')).toBeNull();
    });

    it('shows the same empty state when there are no wallets at all', () => {
      const fixture = createWithoutPrimary({ wallets: noWallets });

      expect(fixture.componentInstance.primaryWalletMissing()).toBe(true);
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Основной кошелёк не назначен');
    });

    it('keeps "Обновить сейчас" enabled without a primary wallet and sends the refresh request', () => {
      const fixture = createWithoutPrimary();
      const button = refreshButton(fixture);

      expect(button.disabled).toBe(false);
      button.click();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`);
      expect(req.request.method).toBe('POST');
      req.flush({ lastSuccessfulUpdate: null, rates: [] });
    });

    it('confirms "Список доступных валют обновлён." (not "Курсы валют обновлены.") when refreshed without a primary wallet', () => {
      const fixture = createWithoutPrimary();
      const component = fixture.componentInstance;

      component.refresh();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush({ lastSuccessfulUpdate: null, rates: [] });
      fixture.detectChanges();

      expect(component.successMessage()).toBe('Список доступных валют обновлён.');
      const toast = (fixture.nativeElement as HTMLElement).querySelector('app-toast');
      expect(toast?.textContent).toContain('Список доступных валют обновлён.');
      // The state remains the "no primary wallet" one: no rows are fabricated.
      expect((fixture.nativeElement as HTMLElement).querySelector('table')).toBeNull();
      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Основной кошелёк не назначен');
    });

    it('confirms "Курсы валют обновлены." when a primary wallet exists, even if the rate list stays empty', () => {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);

      fixture.componentInstance.refresh();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush({ lastSuccessfulUpdate: null, rates: [] });

      expect(fixture.componentInstance.successMessage()).toBe('Курсы валют обновлены.');
    });

    it('a 502 with no data and no primary wallet shows the list-update message inside the empty state (as an alert)', () => {
      const fixture = createWithoutPrimary();
      const component = fixture.componentInstance;

      component.refresh();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`)
        .flush({ lastSuccessfulUpdate: null, rates: [] }, { status: 502, statusText: 'Bad Gateway' });
      fixture.detectChanges();

      expect(component.sourceUnavailableEmpty()).toBe('Не удалось обновить список доступных валют.');
      const root = fixture.nativeElement as HTMLElement;
      const empty = root.querySelector('app-empty-state')!;
      expect(empty.textContent).toContain('Основной кошелёк не назначен');
      const status = empty.querySelector('[role="alert"]');
      expect(status?.textContent).toContain('Не удалось обновить список доступных валют.');
      // No success toast and no "source unavailable" badge for the empty case.
      expect(root.querySelector('app-toast')).toBeNull();
      expect(component.successMessage()).toBeNull();
    });

    it('a 502 problem+json without a rates payload and no data lands in the empty state with the server text', () => {
      const fixture = createWithoutPrimary();
      const component = fixture.componentInstance;

      component.refresh();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`)
        .flush({ title: 'Bad Gateway', status: 502, detail: 'Сервис курсов недоступен.' }, { status: 502, statusText: 'Bad Gateway' });
      fixture.detectChanges();

      expect(component.sourceUnavailableEmpty()).toBe('Сервис курсов недоступен.');
      expect(component.refreshError()).toBeNull();
      expect((fixture.nativeElement as HTMLElement).querySelector('app-empty-state [role="alert"]')?.textContent).toContain(
        'Сервис курсов недоступен.',
      );
    });

    it('a retry after a failed 502 clears the empty-state message', () => {
      const fixture = createWithoutPrimary();
      const component = fixture.componentInstance;

      component.refresh();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`)
        .flush({ lastSuccessfulUpdate: null, rates: [] }, { status: 502, statusText: 'Bad Gateway' });
      expect(component.sourceUnavailableEmpty()).not.toBeNull();

      component.refresh();
      expect(component.sourceUnavailableEmpty()).toBeNull();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush({ lastSuccessfulUpdate: null, rates: [] });
      fixture.detectChanges();

      expect(component.successMessage()).toBe('Список доступных валют обновлён.');
      expect((fixture.nativeElement as HTMLElement).querySelector('app-empty-state [role="alert"]')).toBeNull();
    });

    it('with no rates yet and a primary wallet, a 502 shows "Курсов ещё нет" with the message and a retry', () => {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);

      fixture.componentInstance.refresh();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`)
        .flush({ lastSuccessfulUpdate: null, rates: [] }, { status: 502, statusText: 'Bad Gateway' });
      fixture.detectChanges();

      const empty = (fixture.nativeElement as HTMLElement).querySelector('app-empty-state')!;
      expect(empty.textContent).toContain('Курсов ещё нет');
      expect(empty.textContent).toContain('Источник курсов недоступен, а прежних данных ещё нет.');
      expect(empty.querySelector('[role="alert"]')?.textContent).toContain('Источник курсов недоступен, а прежних данных ещё нет.');
      expect(Array.from(empty.querySelectorAll('button')).map((b) => b.textContent?.trim())).toContain('Повторить');
    });

    it('without a primary wallet but with a previous successful update a 502 never claims that the last rates are shown', () => {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: '2026-09-10T12:00:00Z', rates: [] });
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush({ data: [], pagination: { nextCursor: null, hasMore: false } });

      fixture.componentInstance.refresh();
      httpMock
        .expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`)
        .flush({ lastSuccessfulUpdate: '2026-09-10T12:00:00Z', rates: [] }, { status: 502, statusText: 'Bad Gateway' });
      fixture.detectChanges();

      const root = fixture.nativeElement as HTMLElement;
      expect(fixture.componentInstance.sourceUnavailable()).toBeNull();
      expect(root.querySelector('app-empty-state [role="alert"]')?.textContent).toContain('Не удалось обновить список доступных валют');
      expect(root.textContent).not.toContain('последние известные курсы');
      expect(root.querySelector('app-badge')).toBeNull();
    });

    it('a failed refresh other than 502 is announced as an alert', () => {
      const fixture = createComponent();
      fixture.componentInstance.refresh();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush('boom', { status: 500, statusText: 'Server Error' });
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).querySelector('p.warning')?.getAttribute('role')).toBe('alert');
    });

    it('with a previous successful update an empty rates list is not titled "Ещё не было"', () => {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: '2026-09-10T12:00:00Z', rates: [] });
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
      fixture.detectChanges();

      const empty = (fixture.nativeElement as HTMLElement).querySelector('app-empty-state')!;
      expect(empty.querySelector('h3')?.textContent?.trim()).toBe('Нет курсов: активна только основная валюта');
      expect(empty.textContent).not.toContain('Ещё не было');
      expect(fixture.componentInstance.lastUpdateLabel()).not.toBe('Ещё не было');
    });

    it('with previous data a 502 shows the new note about rates or the available list, plus the badge', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.refresh();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush(
        { lastSuccessfulUpdate: '2026-09-10T12:00:00Z', rates: [{ fromCurrencyId: 'cur-usd', toCurrencyId: 'cur-eur', rate: '0.9123' }] },
        { status: 502, statusText: 'Bad Gateway' },
      );
      fixture.detectChanges();

      const root = fixture.nativeElement as HTMLElement;
      const note = root.querySelector('.status-note--warning');
      expect(note?.getAttribute('role')).toBe('alert');
      expect(note?.textContent?.trim()).toBe('Курсы или список доступных валют обновить не удалось. Показаны последние известные курсы.');
      expect(root.querySelector('app-badge')?.textContent).toContain('Источник недоступен');
      expect(root.querySelector('table')).not.toBeNull();
    });

    it('a 502 whose body is not a rates payload (gateway) does not touch the rows already on screen', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.refresh();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/refresh`).flush('<html>Bad Gateway</html>', { status: 502, statusText: 'Bad Gateway' });

      expect(component.rows()).toEqual([{ fromCurrencyCode: 'USD', toCurrencyCode: 'EUR', rate: '0.9123' }]);
      expect(component.sourceUnavailable()).not.toBeNull();
      expect(component.refreshError()).toBeNull();
    });

    it('the first-ever refresh with a primary wallet and no previous run explains that it loads rates and the available list', () => {
      const fixture = TestBed.createComponent(ExchangeRatesComponent);
      fixture.detectChanges();
      httpMock.expectOne(`${environment.apiBaseUrl}/exchange-rates/latest`).flush({ lastSuccessfulUpdate: null, rates: [] });
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
      fixture.detectChanges();

      const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
      expect(text).toContain('Ещё не было');
      expect(text).toContain('Нажмите «Обновить сейчас», чтобы получить актуальные курсы и список доступных валют.');
    });
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
