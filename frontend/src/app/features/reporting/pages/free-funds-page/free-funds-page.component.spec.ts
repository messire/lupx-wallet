import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { FreeFundsComponent } from './free-funds-page.component';

const api = environment.apiBaseUrl;
const rub = (amount: string) => ({ amount, currencyId: 'c-rub', currencyCode: 'RUB' });
const emptyPage = { data: [], pagination: { nextCursor: null, hasMore: false } };

describe('FreeFundsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [FreeFundsComponent, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  function create() {
    const fixture = TestBed.createComponent(FreeFundsComponent);
    fixture.detectChanges();
    httpMock.match((req) => !req.url.startsWith(`${api}/reporting`)).forEach((req) => req.flush(emptyPage));
    return fixture;
  }

  it('shows the totals, the deficit and the upcoming payments with overdue items, without subtracting them', () => {
    const fixture = create();
    httpMock.expectOne(`${api}/reporting/free-funds`).flush({
      actualAmount: rub('1000.00'),
      reservedAmount: rub('1200.00'),
      freeAmount: rub('-200.00'),
      deficitAmount: rub('200.00'),
      ratesAsOfDate: '2026-11-14',
      rateSource: 'Frankfurter',
      rates: [],
      excludedWallets: [],
      wallets: [
        {
          walletId: 'w-1',
          currencyId: 'c-rub',
          includeInTotal: true,
          isPrimary: true,
          balance: rub('1000.00'),
          reserved: rub('1200.00'),
          free: rub('-200.00'),
          deficit: rub('200.00'),
        },
      ],
    });
    const upcoming = httpMock.expectOne((req) => req.url === `${api}/reporting/upcoming-payments`);
    expect(upcoming.request.params.get('days')).toBe('30');
    upcoming.flush({
      from: '2026-11-14',
      to: '2026-12-14',
      totalAmount: rub('500.00'),
      overdueAmount: rub('100.00'),
      ratesAsOfDate: '2026-11-14',
      rateSource: 'Frankfurter',
      rates: [],
      missingRates: [],
      items: [
        { kind: 'LoanPayment', date: '2026-11-01', isOverdue: true, amount: rub('100.00'), convertedAmount: rub('100.00'), loanName: 'Ипотека', creditorName: 'Банк' },
        { kind: 'PlannedExpense', date: '2026-11-20', isOverdue: false, amount: rub('400.00'), convertedAmount: rub('400.00'), plannedTransactionId: 'p-1' },
      ],
    });
    fixture.detectChanges();

    const component = fixture.componentInstance;
    expect(component.hasDeficit()).toBe(true);
    expect(component.overdueCount()).toBe(1);
    const text = ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('не вычитаются из свободных средств');
    expect(text).toContain('Ипотека · Банк');
    expect(text).toContain('Просрочен');
    expect(text).toContain('-200.00 RUB');
  });

  it('reloads the upcoming payments when the horizon changes', () => {
    const fixture = create();
    httpMock.expectOne(`${api}/reporting/free-funds`).flush(null, { status: 404, statusText: 'Not Found' });
    httpMock.expectOne((req) => req.url === `${api}/reporting/upcoming-payments`).flush(null, { status: 404, statusText: 'Not Found' });
    expect(fixture.componentInstance.noPrimaryWallet()).toBe(true);

    fixture.componentInstance.daysControl.setValue('7');
    const req = httpMock.expectOne((r) => r.url === `${api}/reporting/upcoming-payments`);
    expect(req.request.params.get('days')).toBe('7');
    req.flush(null, { status: 404, statusText: 'Not Found' });
  });
});
