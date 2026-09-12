import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { BalanceHistoryComponent } from './balance-history.component';

const wallet = {
  id: 'wallet-1',
  name: 'Наличные',
  walletTypeId: 'wt-1',
  purposeDescription: null,
  currencyId: 'currency-1',
  initialBalance: { amount: '100.00', currencyId: 'currency-1' },
  accountingStartDate: '2026-01-01',
  currentBalance: { amount: '150.00', currencyId: 'currency-1' },
  includeInTotal: true,
  isPrimary: true,
  isArchived: false,
  displayOrder: 0,
  color: null,
  icon: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

describe('BalanceHistoryComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [BalanceHistoryComponent, HttpClientTestingModule],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent() {
    const fixture = TestBed.createComponent(BalanceHistoryComponent);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`).flush({
      data: [wallet],
      pagination: { nextCursor: null, hasMore: false },
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush({
      data: [{ id: 'currency-1', code: 'USD', name: 'US Dollar', isActive: true }],
      pagination: { nextCursor: null, hasMore: false },
    });

    fixture.detectChanges();
    return fixture;
  }

  function selectWallet(fixture: ReturnType<typeof createComponent>): void {
    const component = fixture.componentInstance;
    component.walletForm.controls.walletId.setValue('wallet-1');
    fixture.detectChanges();
  }

  it('should create and load wallets/currencies', () => {
    const fixture = createComponent();
    expect(fixture.componentInstance.wallets().length).toBe(1);
    expect(fixture.componentInstance.currencies().length).toBe(1);
  });

  it('shows a prompt to select a wallet before any wallet is chosen', () => {
    const fixture = createComponent();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.empty')?.textContent).toContain('Выберите кошелек');
  });

  it('shows the accounting-start-date hint (Q13) once a wallet is selected', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const compiled = fixture.nativeElement as HTMLElement;
    const hint = compiled.querySelector('.hint')?.textContent ?? '';
    expect(hint).toContain('2026-01-01');
    expect(hint).toContain('считается равным 0');
  });

  it('requests balance on date and displays the result', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const component = fixture.componentInstance;
    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.showBalanceOnDate();

    const req = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-06-01',
    );
    req.flush({ walletId: 'wallet-1', date: '2026-06-01', balance: { amount: '75.00', currencyId: 'currency-1' } });
    fixture.detectChanges();

    expect(component.balanceSnapshot()?.balance.amount).toBe('75.00');
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance-result')?.textContent).toContain('75.00');
  });

  it('flags a balance-on-date result that falls before the accounting start date (Q13)', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const component = fixture.componentInstance;
    component.balanceDateForm.controls.date.setValue('2025-01-01');
    component.showBalanceOnDate();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance`)
      .flush({ walletId: 'wallet-1', date: '2025-01-01', balance: { amount: '0', currencyId: 'currency-1' } });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance-result .badge')).not.toBeNull();
  });

  it('does not send a request when from > to and shows a validation error', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const component = fixture.componentInstance;
    component.rangeForm.setValue({ from: '2026-06-01', to: '2026-01-01' });
    component.showHistory();

    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`);
    expect(component.rangeValidationError()).toContain('не может быть позже');
  });

  it('loads history for a valid range and supports load-more via cursor', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const component = fixture.componentInstance;
    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();

    const req = httpMock.expectOne(
      (r) =>
        r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` &&
        r.params.get('from') === '2026-01-01' &&
        r.params.get('to') === '2026-01-31',
    );
    req.flush({
      data: [{ walletId: 'wallet-1', date: '2026-01-01', balance: { amount: '10.00', currencyId: 'currency-1' } }],
      pagination: { nextCursor: 'cursor-1', hasMore: true },
    });
    fixture.detectChanges();

    expect(component.history().length).toBe(1);
    expect(component.hasMore()).toBe(true);

    component.loadMoreHistory();
    const moreReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` && r.params.get('cursor') === 'cursor-1',
    );
    moreReq.flush({
      data: [{ walletId: 'wallet-1', date: '2026-01-02', balance: { amount: '20.00', currencyId: 'currency-1' } }],
      pagination: { nextCursor: null, hasMore: false },
    });
    fixture.detectChanges();

    expect(component.history().length).toBe(2);
    expect(component.hasMore()).toBe(false);
  });

  it('surfaces a server 400 (e.g. unexpected from > to) via problem-details', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const component = fixture.componentInstance;
    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`);
    req.flush(
      { title: 'Bad Request', status: 400, detail: 'Диапазон дат некорректен.' },
      { status: 400, statusText: 'Bad Request' },
    );
    fixture.detectChanges();

    expect(component.historyLoadError()).toBe('Диапазон дат некорректен.');
  });

  it('resets results when the selected wallet changes', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const component = fixture.componentInstance;
    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`)
      .flush({
        data: [{ walletId: 'wallet-1', date: '2026-01-01', balance: { amount: '10.00', currencyId: 'currency-1' } }],
        pagination: { nextCursor: null, hasMore: false },
      });
    fixture.detectChanges();
    expect(component.history().length).toBe(1);

    component.walletForm.controls.walletId.setValue('');
    fixture.detectChanges();

    expect(component.history().length).toBe(0);
    expect(component.selectedWallet()).toBeNull();
  });
});
