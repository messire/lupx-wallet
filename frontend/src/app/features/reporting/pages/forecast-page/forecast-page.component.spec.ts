import { HttpClientTestingModule, HttpTestingController, TestRequest } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { CashFlowForecast } from '../../../../data-access/reporting/reporting-api.models';
import { ForecastComponent } from './forecast-page.component';

const forecastUrl = `${environment.apiBaseUrl}/reporting/cash-flow-forecast`;
const rub = (amount: string) => ({ amount, currencyId: 'c-rub', currencyCode: 'RUB' });

function forecast(months: 1 | 3 | 6 | 12, overrides: Partial<CashFlowForecast> = {}): CashFlowForecast {
  return {
    from: '2026-11-14',
    to: '2027-02-14',
    horizonMonths: months,
    startBalance: rub('1000.00'),
    endBalance: rub('-200.00'),
    minBalance: rub('-200.00'),
    minBalanceDate: '2026-12-01',
    firstNegativeDate: '2026-12-01',
    negativePeriods: [{ from: '2026-12-01', to: '2027-02-14' }],
    days: [
      { date: '2026-11-14', netChange: rub('0'), expectedBalance: rub('1000.00'), isNegative: false, movements: [] },
      {
        date: '2026-12-01',
        netChange: rub('-1200.00'),
        expectedBalance: rub('-200.00'),
        isNegative: true,
        movements: [
          {
            kind: 'LoanPayment',
            date: '2026-12-01',
            originalDate: '2026-12-01',
            isOverdue: false,
            amount: rub('-1200.00'),
            convertedAmount: rub('-1200.00'),
            loanName: 'Ипотека',
          },
        ],
      },
    ],
    ratesAsOfDate: '2026-11-14',
    rateSource: 'Frankfurter',
    rates: [],
    assumptions: ['Курсы на 14.11.2026, будущие изменения курсов не учитываются.'],
    excludedWallets: [],
    missingRates: [],
    ...overrides,
  };
}

describe('ForecastComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ForecastComponent, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
  });

  function create() {
    const fixture = TestBed.createComponent(ForecastComponent);
    fixture.detectChanges();
    // Lookups are not essential for the forecast itself.
    httpMock
      .match((req) => !req.url.startsWith(forecastUrl))
      .forEach((req: TestRequest) => req.flush({ data: [], pagination: { nextCursor: null, hasMore: false } }));
    return fixture;
  }

  it('requests a 3-month forecast by default and shows the first negative date, movements and assumptions', () => {
    const fixture = create();
    const req = httpMock.expectOne((r) => r.url === forecastUrl);
    expect(req.request.params.get('months')).toBe('3');
    req.flush(forecast(3));
    fixture.detectChanges();

    const text = ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toMatch(/Первая дата с минусом\s*01\.12\.2026/);
    expect(text).toContain('Ипотека');
    expect(text).toContain('будущие изменения курсов не учитываются');
    expect(fixture.componentInstance.movementDays()).toHaveLength(1);
    httpMock.verify();
  });

  it('ignores a late answer for a previous horizon', () => {
    const fixture = create();
    const first = httpMock.expectOne((r) => r.url === forecastUrl);

    fixture.componentInstance.selectHorizon(12);
    const second = httpMock.expectOne((r) => r.url === forecastUrl && r.params.get('months') === '12');
    second.flush(forecast(12));
    first.flush(forecast(3));

    expect(fixture.componentInstance.forecast()?.horizonMonths).toBe(12);
    httpMock.verify();
  });

  it('explains a missing primary wallet (404) instead of an error', () => {
    const fixture = create();
    httpMock.expectOne((r) => r.url === forecastUrl).flush(null, { status: 404, statusText: 'Not Found' });
    expect(fixture.componentInstance.noPrimaryWallet()).toBe(true);
    expect(fixture.componentInstance.loadError()).toBeNull();
    httpMock.verify();
  });
});
