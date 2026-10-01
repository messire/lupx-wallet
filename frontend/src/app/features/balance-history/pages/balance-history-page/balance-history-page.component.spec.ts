import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { Subject } from 'rxjs';
import { environment } from '../../../../../environments/environment';
import { BalanceHistoryComponent } from './balance-history-page.component';

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
      data: [{ id: 'currency-1', code: 'USD', name: 'US Dollar' }],
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
    expect(compiled.querySelector('.app-empty-state__title')?.textContent).toContain('Выберите кошелек');
  });

  it('shows the accounting-start-date hint (Q13) once a wallet is selected', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const compiled = fixture.nativeElement as HTMLElement;
    const hint = compiled.querySelector('.wallet-accounting-note')?.textContent ?? '';
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
    expect(compiled.querySelector('.balance-result app-badge')).not.toBeNull();
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

  // The public showBalanceOnDate()/showHistory() re-entrancy guard (`if (...
  // this.balanceLoading()) return;`) already blocks a *second click of the same
  // button* while a request is in flight, so that particular path can never race
  // in practice. The tests below instead exercise the underlying `Subject` +
  // `switchMap` pipeline directly (bypassing the guard via the private trigger
  // subjects) to prove the race-protection mechanism itself: a superseded
  // in-flight request is unsubscribed (and so can never deliver a "late" response)
  // as soon as a newer request for the same scenario is issued — e.g. via wallet
  // change (B7) racing with an already-submitted query, which the guard cannot
  // prevent since it resets `*Loading` back to `false`.
  it('cancels a superseded balance-on-date request as soon as a newer one is issued (race protection, B6)', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const trigger = (fixture.componentInstance as unknown as { balanceDateTrigger$: Subject<unknown> }).balanceDateTrigger$;

    trigger.next({ walletId: 'wallet-1', date: '2026-01-01' });
    const firstReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-01-01',
    );

    trigger.next({ walletId: 'wallet-1', date: '2026-02-01' });
    const secondReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-02-01',
    );

    // The superseded request is cancelled (its subscription torn down by switchMap)
    // — it can no longer deliver a late response that would overwrite the newer one.
    expect(firstReq.cancelled).toBe(true);

    secondReq.flush({ walletId: 'wallet-1', date: '2026-02-01', balance: { amount: '20.00', currencyId: 'currency-1' } });
    fixture.detectChanges();
    expect(fixture.componentInstance.balanceSnapshot()?.date).toBe('2026-02-01');
  });

  it('cancels a superseded history request as soon as a newer one is issued (race protection, B6)', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const trigger = (fixture.componentInstance as unknown as { historyTrigger$: Subject<unknown> }).historyTrigger$;

    trigger.next({ walletId: 'wallet-1', from: '2026-01-01', to: '2026-01-10', mode: 'replace' });
    const firstReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` && r.params.get('to') === '2026-01-10',
    );

    trigger.next({ walletId: 'wallet-1', from: '2026-02-01', to: '2026-02-10', mode: 'replace' });
    const secondReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` && r.params.get('to') === '2026-02-10',
    );

    expect(firstReq.cancelled).toBe(true);

    secondReq.flush({
      data: [{ walletId: 'wallet-1', date: '2026-02-01', balance: { amount: '20.00', currencyId: 'currency-1' } }],
      pagination: { nextCursor: null, hasMore: false },
    });
    fixture.detectChanges();
    expect(fixture.componentInstance.history().map((snapshot) => snapshot.date)).toEqual(['2026-02-01']);
  });

  it('cancels an in-flight balance-on-date request when the wallet changes (B7)', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-01-01');
    component.showBalanceOnDate();
    const req = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-01-01',
    );

    component.walletForm.controls.walletId.setValue('');
    fixture.detectChanges();

    // switchMap unsubscribes the in-flight request as soon as the wallet changes
    // (resetResults() pushes a cancelling `null` through the same trigger subject)
    // — it is aborted and can no longer deliver a late response for a wallet that
    // is no longer selected.
    expect(req.cancelled).toBe(true);
    expect(component.balanceSnapshot()).toBeNull();
  });

  it('cancels an in-flight history request when the wallet changes (B7)', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();
    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`);

    component.walletForm.controls.walletId.setValue('');
    fixture.detectChanges();

    expect(req.cancelled).toBe(true);
    expect(component.history().length).toBe(0);
  });

  // Plan B5: the caption of an already-shown result must reflect the request
  // that produced it, not the current (possibly-since-edited) form value. The
  // component achieves this by rendering the caption from the response itself
  // (`snapshot.date`, a signal set once per response) rather than re-reading
  // the form — so it is stable by construction once a response has landed,
  // independent of the race-protection tests above.
  it('keeps the balance-on-date caption tied to the request that produced it, even after the date field is edited afterward', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.showBalanceOnDate();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-06-01')
      .flush({ walletId: 'wallet-1', date: '2026-06-01', balance: { amount: '75.00', currencyId: 'currency-1' } });
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance-result')?.textContent).toContain('2026-06-01');

    // Edit the date field after the response landed — no new request is sent
    // (showBalanceOnDate() was not called again), so the caption of the
    // already-shown result must not change.
    component.balanceDateForm.controls.date.setValue('2026-07-15');
    fixture.detectChanges();

    expect(component.balanceSnapshot()?.date).toBe('2026-06-01');
    expect(compiled.querySelector('.balance-result')?.textContent).toContain('2026-06-01');
    expect(compiled.querySelector('.balance-result')?.textContent).not.toContain('2026-07-15');
    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-07-15');
  });

  it('keeps the applied history period caption (appliedRangeFrom/To) stable after the range fields are edited afterward', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`)
      .flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    expect(component.appliedRangeFrom()).toBe('2026-01-01');
    expect(component.appliedRangeTo()).toBe('2026-01-31');

    // Editing the range fields after the response must not retroactively change
    // the caption of the period that was actually applied — only a fresh
    // "Показать историю" submission may do that.
    component.rangeForm.setValue({ from: '2026-03-01', to: '2026-03-31' });
    fixture.detectChanges();

    expect(component.appliedRangeFrom()).toBe('2026-01-01');
    expect(component.appliedRangeTo()).toBe('2026-01-31');
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.history-period')?.textContent).toContain('2026-01-01');
    expect(compiled.querySelector('.history-period')?.textContent).not.toContain('2026-03-01');
  });

  // Plan B4: an unknown balance must render as "—", never as a fabricated 0 or
  // an empty string, across all three states where no real result is available.
  it('shows "—" before the first balance-on-date request', () => {
    const fixture = createComponent();
    selectWallet(fixture);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance')?.textContent?.trim()).toBe('—');
  });

  it('does not render any balance value (in particular no fabricated 0) while a balance-on-date request is loading', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.showBalanceOnDate();
    fixture.detectChanges();

    // While loading, the template swaps the `.balance` paragraph for a loading
    // skeleton (see balance-history-page.component.html) — no numeric value,
    // fabricated or otherwise, is rendered at all during this state.
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance')).toBeNull();
    expect(compiled.querySelector('app-skeleton')).not.toBeNull();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance`)
      .flush({ walletId: 'wallet-1', date: '2026-06-01', balance: { amount: '10.00', currencyId: 'currency-1' } });
  });

  it('shows "—" (not 0 and not a stale value) when a balance-on-date request fails with a server error', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.showBalanceOnDate();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance`)
      .flush({ title: 'Server error', status: 500, detail: 'Внутренняя ошибка сервера.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.balanceSnapshot()).toBeNull();
    const compiled = fixture.nativeElement as HTMLElement;
    const balanceText = compiled.querySelector('.balance')?.textContent?.trim();
    expect(balanceText).toBe('—');
    expect(balanceText).not.toContain('0');
  });

  // Plan B4 (client side of the rule): the client must never invent a 0 itself —
  // a 0 balance can only ever come from an actual API response.
  it('never substitutes a client-side 0 for a missing result — the "unknown" state stays "—" through an initial load and a failed request', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;
    const compiled = fixture.nativeElement as HTMLElement;

    expect(component.balanceSnapshot()).toBeNull();
    expect(compiled.querySelector('.balance')?.textContent?.trim()).toBe('—');

    component.balanceDateForm.controls.date.setValue('2025-01-01');
    component.showBalanceOnDate();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance`)
      .flush({ title: 'Server error', status: 500, detail: 'Ошибка сервера.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.balanceSnapshot()).toBeNull();
    expect(compiled.querySelector('.balance')?.textContent?.trim()).toBe('—');
  });

  it('renders a real 0 balance returned by the API as an actual value, not as the "—" unknown placeholder', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2025-01-01');
    component.showBalanceOnDate();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance`)
      .flush({ walletId: 'wallet-1', date: '2025-01-01', balance: { amount: '0', currencyId: 'currency-1' } });
    fixture.detectChanges();

    expect(component.balanceSnapshot()?.balance.amount).toBe('0');
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance')?.textContent?.trim()).not.toBe('—');
  });

  it('requires both dates for the history range — a single filled date shows a validation error without calling the API', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.rangeForm.setValue({ from: '2026-01-01', to: '' });
    component.showHistory();

    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`);
    expect(component.rangeValidationError()).toContain('Укажите обе даты');
  });

  it('clears both form dates (balance-on-date and history range), not just the results, when the wallet changes (B7)', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    fixture.detectChanges();
    expect(component.balanceDateForm.controls.date.value).toBe('2026-06-01');
    expect(component.rangeForm.controls.from.value).toBe('2026-01-01');
    expect(component.rangeForm.controls.to.value).toBe('2026-01-31');

    component.walletForm.controls.walletId.setValue('');
    fixture.detectChanges();

    expect(component.balanceDateForm.controls.date.value).toBe('');
    expect(component.rangeForm.controls.from.value).toBe('');
    expect(component.rangeForm.controls.to.value).toBe('');
  });

  // MINOR-4 (code review of plan B5): a stale error from a failed "Load more"
  // attempt must not remain visible once the retried page has loaded successfully.
  it('clears a "Load more" error once the retried page loads successfully', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`)
      .flush({
        data: [{ walletId: 'wallet-1', date: '2026-01-01', balance: { amount: '10.00', currencyId: 'currency-1' } }],
        pagination: { nextCursor: 'cursor-1', hasMore: true },
      });
    fixture.detectChanges();

    component.loadMoreHistory();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` && r.params.get('cursor') === 'cursor-1')
      .flush({ title: 'Server error', status: 500, detail: 'Ошибка сервера.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(component.historyLoadError()).toBeTruthy();

    component.loadMoreHistory();
    fixture.detectChanges();
    expect(component.historyLoadError()).toBeNull();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('app-error-state')).toBeNull();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` && r.params.get('cursor') === 'cursor-1')
      .flush({
        data: [{ walletId: 'wallet-1', date: '2026-01-02', balance: { amount: '20.00', currencyId: 'currency-1' } }],
        pagination: { nextCursor: null, hasMore: false },
      });
    fixture.detectChanges();

    expect(component.historyLoadError()).toBeNull();
    expect(component.history().length).toBe(2);
  });

  // MINOR-5.1: "Повторить" must replay the request that actually failed, not
  // whatever the form currently holds — otherwise editing a field after a
  // failure but before clicking retry silently sends different parameters
  // than the ones the user was shown an error for.
  it('retries a failed balance-on-date request with its original parameters, not a since-edited form value', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.showBalanceOnDate();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-06-01')
      .flush({ title: 'Server error', status: 500, detail: 'Ошибка сервера.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(component.balanceError()).toBeTruthy();

    // Field is edited after the failure but before "Повторить" is clicked.
    component.balanceDateForm.controls.date.setValue('2026-07-15');
    fixture.detectChanges();

    component.retryBalanceOnDate();
    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-07-15');
    const retryReq = httpMock.expectOne(
      (r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-06-01',
    );
    retryReq.flush({ walletId: 'wallet-1', date: '2026-06-01', balance: { amount: '75.00', currencyId: 'currency-1' } });
    fixture.detectChanges();

    expect(component.balanceSnapshot()?.date).toBe('2026-06-01');
  });

  it('retries a failed history request with its original range, not a since-edited form value', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.rangeForm.setValue({ from: '2026-01-01', to: '2026-01-31' });
    component.showHistory();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history`)
      .flush({ title: 'Server error', status: 500, detail: 'Ошибка сервера.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(component.historyLoadError()).toBeTruthy();

    // Range is edited after the failure but before "Повторить" is clicked.
    component.rangeForm.setValue({ from: '2026-03-01', to: '2026-03-31' });
    fixture.detectChanges();

    component.retryHistory();
    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` && r.params.get('from') === '2026-03-01');
    const retryReq = httpMock.expectOne(
      (r) =>
        r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance-history` &&
        r.params.get('from') === '2026-01-01' &&
        r.params.get('to') === '2026-01-31',
    );
    retryReq.flush({
      data: [{ walletId: 'wallet-1', date: '2026-01-01', balance: { amount: '10.00', currencyId: 'currency-1' } }],
      pagination: { nextCursor: null, hasMore: false },
    });
    fixture.detectChanges();

    expect(component.history().length).toBe(1);
  });

  // MINOR-5.2: while a new balance-on-date request is loading, the caption must
  // not display the date of a previous (unrelated) result — that would be read
  // as describing the value currently loading.
  it('does not show the previous result\'s date as a caption while a new balance-on-date request is loading', () => {
    const fixture = createComponent();
    selectWallet(fixture);
    const component = fixture.componentInstance;

    component.balanceDateForm.controls.date.setValue('2026-06-01');
    component.showBalanceOnDate();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-06-01')
      .flush({ walletId: 'wallet-1', date: '2026-06-01', balance: { amount: '75.00', currencyId: 'currency-1' } });
    fixture.detectChanges();

    component.balanceDateForm.controls.date.setValue('2026-07-15');
    component.showBalanceOnDate();
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('.balance-result')?.textContent).not.toContain('2026-06-01');

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets/wallet-1/balance` && r.params.get('date') === '2026-07-15')
      .flush({ walletId: 'wallet-1', date: '2026-07-15', balance: { amount: '90.00', currencyId: 'currency-1' } });
  });
});
