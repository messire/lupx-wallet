import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../environments/environment';
import { ReportingComponent } from './reporting.component';

const walletsResponse = {
  data: [
    { id: 'wallet-cash', name: 'Наличные', currencyId: 'cur-usd' },
    { id: 'wallet-crypto', name: 'Крипта', currencyId: 'cur-btc' },
  ],
  pagination: { nextCursor: null, hasMore: false },
};

const currenciesResponse = {
  data: [
    { id: 'cur-usd', code: 'USD', name: 'US Dollar', isActive: true },
    { id: 'cur-btc', code: 'BTC', name: 'Bitcoin', isActive: true },
  ],
  pagination: { nextCursor: null, hasMore: false },
};

describe('ReportingComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReportingComponent, HttpClientTestingModule],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent() {
    const fixture = TestBed.createComponent(ReportingComponent);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush(walletsResponse);
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush(currenciesResponse);

    return fixture;
  }

  it('loads and displays the current total amount (UC-19)', () => {
    const fixture = createComponent();

    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/reporting/total-amount`)
      .flush({
        date: null,
        amount: { amount: '1234.56', currencyId: 'cur-usd' },
        ratesAsOfDate: '2026-09-11',
        excludedWallets: [],
      });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.currentTotal()?.amount.amount).toBe('1234.56');
    expect(component.currencyCode('cur-usd')).toBe('USD');
    expect(component.currentNoPrimaryWallet()).toBe(false);
  });

  it('shows a friendly message when there is no primary wallet (404), not a raw error', () => {
    const fixture = createComponent();

    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/reporting/total-amount`)
      .flush(null, { status: 404, statusText: 'Not Found' });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.currentNoPrimaryWallet()).toBe(true);
    expect(component.currentError()).toBeNull();
  });

  it('shows excludedWallets as a warning, resolving wallet names via WalletsService', () => {
    const fixture = createComponent();

    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/reporting/total-amount`)
      .flush({
        date: null,
        amount: { amount: '100', currencyId: 'cur-usd' },
        ratesAsOfDate: '2026-09-11',
        excludedWallets: [{ walletId: 'wallet-crypto', reason: 'Курс валюты недоступен.' }],
      });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.currentTotal()?.excludedWallets).toEqual([{ walletId: 'wallet-crypto', reason: 'Курс валюты недоступен.' }]);
    expect(component.walletName('wallet-crypto')).toBe('Крипта');
  });

  it('loads the historical total amount for a selected date (UC-21) and flags a differing ratesAsOfDate', () => {
    const fixture = createComponent();

    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/reporting/total-amount`).flush({
      date: null,
      amount: { amount: '100', currencyId: 'cur-usd' },
      ratesAsOfDate: '2026-09-11',
      excludedWallets: [],
    });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    component.dateForm.controls.date.setValue('2026-05-01');
    component.showHistorical();

    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/reporting/total-amount` && req.params.get('date') === '2026-05-01')
      .flush({
        date: '2026-05-01',
        amount: { amount: '80', currencyId: 'cur-usd' },
        ratesAsOfDate: '2026-04-20',
        excludedWallets: [],
      });
    fixture.detectChanges();

    const total = component.historicalTotal();
    expect(total?.amount.amount).toBe('80');
    expect(component.ratesNote(total!)).toBe('2026-04-20');
  });
});
