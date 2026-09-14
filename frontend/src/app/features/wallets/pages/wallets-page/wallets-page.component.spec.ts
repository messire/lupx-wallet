import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsComponent } from './wallets-page.component';

const primaryWallet: Wallet = {
  id: 'w-primary',
  name: 'Основной кошелек',
  walletTypeId: 'wt-1',
  purposeDescription: null,
  currencyId: 'c-usd',
  initialBalance: { amount: '0', currencyId: 'c-usd' },
  accountingStartDate: '2026-01-01',
  currentBalance: { amount: '100', currencyId: 'c-usd' },
  includeInTotal: true,
  isPrimary: true,
  isArchived: false,
  displayOrder: 0,
  color: null,
  icon: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const secondaryWallet: Wallet = {
  ...primaryWallet,
  id: 'w-secondary',
  name: 'Второй кошелек',
  isPrimary: false,
};

const archivedWallet: Wallet = {
  ...primaryWallet,
  id: 'w-archived',
  name: 'Архивный кошелек',
  isPrimary: false,
  isArchived: true,
};

describe('WalletsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [WalletsComponent, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent(wallets: Wallet[] = [primaryWallet, secondaryWallet]) {
    const fixture = TestBed.createComponent(WalletsComponent);
    fixture.detectChanges();

    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`)
      .flush({ data: wallets, pagination: { nextCursor: null, hasMore: false } });
    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/wallet-types`)
      .flush({ data: [{ id: 'wt-1', name: 'Наличные', isActive: true }], pagination: { nextCursor: null, hasMore: false } });
    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [{ id: 'c-usd', code: 'USD', name: 'US Dollar', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    fixture.detectChanges();
    return fixture;
  }

  it('loads wallets without includeArchived by default', () => {
    const fixture = createComponent();
    expect(fixture.componentInstance.wallets().length).toBe(2);
  });

  it('reloads with includeArchived=true when the archived filter is toggled on', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleShowArchived();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets`);
    expect(req.request.params.get('includeArchived')).toBe('true');
    req.flush({ data: [primaryWallet, secondaryWallet, archivedWallet], pagination: { nextCursor: null, hasMore: false } });

    expect(component.showArchived()).toBe(true);
    expect(component.wallets().length).toBe(3);
  });

  it('does not offer archiving for an already archived wallet', () => {
    const fixture = createComponent([primaryWallet, archivedWallet]);
    const compiled = fixture.nativeElement as HTMLElement;

    const cards = Array.from(compiled.querySelectorAll('.wallet-card'));
    const archivedCard = cards.find((card) => card.textContent?.includes('Архивный кошелек'));
    const buttonLabels = Array.from(archivedCard!.querySelectorAll('button')).map((b) => b.textContent?.trim());

    expect(buttonLabels).not.toContain('Архивировать');
  });

  it('disables includeInTotal in the edit form for the primary wallet (Q8)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openEditForm(primaryWallet);

    expect(component.editForm.controls.includeInTotal.disabled).toBe(true);
  });

  it('sends a PATCH request on submitEdit and reloads the list', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openEditForm(secondaryWallet);
    component.editForm.patchValue({ name: 'Обновленное имя' });
    component.submitEdit();

    const patchReq = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/w-secondary`);
    expect(patchReq.request.method).toBe('PATCH');
    expect(patchReq.request.body.name).toBe('Обновленное имя');
    patchReq.flush({ ...secondaryWallet, name: 'Обновленное имя' });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets`)
      .flush({ data: [primaryWallet, { ...secondaryWallet, name: 'Обновленное имя' }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.editingWallet()).toBeNull();
  });

  it('shows a readable error when archiving the primary wallet returns 409', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestConfirm(primaryWallet.id, 'archive');
    expect(component.pendingConfirm()).toEqual({ walletId: primaryWallet.id, action: 'archive' });

    component.confirmPendingAction();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/w-primary/archive`);
    expect(req.request.method).toBe('POST');
    req.flush(
      { title: 'Conflict', status: 409, detail: 'Нельзя архивировать текущий основной кошелек.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.actionError()).toBe('Нельзя архивировать текущий основной кошелек.');
    expect(component.pendingConfirm()).toBeNull();
  });

  it('sets a wallet as primary and shows a readable error for an archived wallet (409)', () => {
    const fixture = createComponent([primaryWallet, archivedWallet]);
    const component = fixture.componentInstance;

    component.setPrimary(archivedWallet);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/w-archived/set-primary`);
    expect(req.request.method).toBe('POST');
    req.flush(
      { title: 'Conflict', status: 409, detail: 'Нельзя назначить основным архивный кошелек.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.actionError()).toBe('Нельзя назначить основным архивный кошелек.');
  });

  it('changes wallet currency and shows a readable error when history exists (409)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openChangeCurrency(secondaryWallet);
    component.currencyForm.patchValue({ currencyId: 'c-eur' });
    component.submitChangeCurrency();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/w-secondary/currency`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ currencyId: 'c-eur' });
    req.flush(
      { title: 'Conflict', status: 409, detail: 'У кошелька уже есть операции или слепки баланса.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.currencyError()).toBe('У кошелька уже есть операции или слепки баланса.');
  });

  it('deletes a wallet after confirmation and shows a readable error on 409', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestConfirm(secondaryWallet.id, 'delete');
    component.confirmPendingAction();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/w-secondary`);
    expect(req.request.method).toBe('DELETE');
    req.flush(
      { title: 'Conflict', status: 409, detail: 'Кошелек имеет историю операций/слепков.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.actionError()).toBe('Кошелек имеет историю операций/слепков.');
  });

  it('deletes a wallet successfully and reloads the list', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestConfirm(secondaryWallet.id, 'delete');
    component.confirmPendingAction();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/w-secondary`);
    req.flush(null, { status: 204, statusText: 'No Content' });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets`)
      .flush({ data: [primaryWallet], pagination: { nextCursor: null, hasMore: false } });

    expect(component.actionError()).toBeNull();
    expect(component.wallets().length).toBe(1);
  });

  // ---- Reference-data loading state (loadWalletTypes / loadCurrencies) ----

  it('loadWalletTypes: shows a loading flag while in flight and clears it on success', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.loadWalletTypes();
    expect(component.walletTypesLoading()).toBe(true);

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallet-types`)
      .flush({ data: [{ id: 'wt-1', name: 'Наличные', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.walletTypesLoading()).toBe(false);
    expect(component.walletTypesLoadError()).toBeNull();
  });

  it('loadWalletTypes: sets a readable error on failure, clears the loading flag, and retry re-fetches', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.loadWalletTypes();
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallet-types`).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.walletTypesLoading()).toBe(false);
    expect(component.walletTypesLoadError()).toBe('Не удалось загрузить типы кошельков.');

    // Retry (same method the "Повторить" button calls) succeeds and clears the error.
    component.loadWalletTypes();
    expect(component.walletTypesLoading()).toBe(true);
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallet-types`)
      .flush({ data: [{ id: 'wt-1', name: 'Наличные', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.walletTypesLoadError()).toBeNull();
    expect(component.walletTypesLoading()).toBe(false);
  });

  it('loadCurrencies: shows a loading flag while in flight and clears it on success', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.loadCurrencies();
    expect(component.currenciesLoading()).toBe(true);

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [{ id: 'c-usd', code: 'USD', name: 'US Dollar', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.currenciesLoading()).toBe(false);
    expect(component.currenciesLoadError()).toBeNull();
  });

  it('loadCurrencies: sets a readable error on failure, clears the loading flag, and retry re-fetches', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.loadCurrencies();
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.currenciesLoading()).toBe(false);
    expect(component.currenciesLoadError()).toBe('Не удалось загрузить валюты.');

    component.loadCurrencies();
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [{ id: 'c-usd', code: 'USD', name: 'US Dollar', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.currenciesLoadError()).toBeNull();
  });

  // ---- Quick-add wallet type (submitNewWalletType) ----

  it('submitNewWalletType: creates the type, selects it in the form, and closes the quick-add row', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleAddWalletType();
    component.newWalletTypeName.set('Кредитная карта');
    component.submitNewWalletType();

    expect(component.isSubmittingWalletType()).toBe(true);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallet-types`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Кредитная карта' });
    req.flush({ id: 'wt-new', name: 'Кредитная карта', isActive: true });

    // submitNewWalletType() reloads the list on success.
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallet-types`)
      .flush({ data: [{ id: 'wt-new', name: 'Кредитная карта', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.isSubmittingWalletType()).toBe(false);
    expect(component.form.controls.walletTypeId.value).toBe('wt-new');
    expect(component.isAddingWalletType()).toBe(false);
    expect(component.newWalletTypeName()).toBe('');
  });

  it('submitNewWalletType: on failure, shows a readable error, ends the loading state, and preserves the typed name', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleAddWalletType();
    component.newWalletTypeName.set('Дублирующее имя');
    component.submitNewWalletType();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallet-types`)
      .flush({ title: 'Conflict', status: 409, detail: 'Тип с таким названием уже существует.' }, { status: 409, statusText: 'Conflict' });

    expect(component.isSubmittingWalletType()).toBe(false);
    expect(component.addWalletTypeError()).toBe('Тип с таким названием уже существует.');
    // The quick-add row stays open and the typed name is not cleared, so the user can fix and resubmit.
    expect(component.isAddingWalletType()).toBe(true);
    expect(component.newWalletTypeName()).toBe('Дублирующее имя');
  });

  it('submitNewWalletType: a second call while the first is in flight does not send a second request', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleAddWalletType();
    component.newWalletTypeName.set('Наличные 2');
    component.submitNewWalletType();
    component.submitNewWalletType(); // guarded by isSubmittingWalletType()

    // If a second request had been sent, expectOne below would fail with "multiple matches".
    httpMock.expectOne(`${environment.apiBaseUrl}/wallet-types`).flush({ id: 'wt-2', name: 'Наличные 2', isActive: true });
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallet-types`)
      .flush({ data: [{ id: 'wt-2', name: 'Наличные 2', isActive: true }], pagination: { nextCursor: null, hasMore: false } });
  });

  // ---- Quick-add currency (submitNewCurrency) ----

  it('submitNewCurrency: creates the currency, selects it in the form, and closes the quick-add row', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleAddCurrency();
    component.newCurrencyIsoCode.setValue('EUR');
    component.submitNewCurrency();

    expect(component.isSubmittingCurrency()).toBe(true);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'EUR', name: 'Евро' });
    req.flush({ id: 'c-eur', code: 'EUR', name: 'Евро', isActive: true });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [{ id: 'c-eur', code: 'EUR', name: 'Евро', isActive: true }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.isSubmittingCurrency()).toBe(false);
    expect(component.form.controls.currencyId.value).toBe('c-eur');
    expect(component.isAddingCurrency()).toBe(false);
    expect(component.newCurrencyIsoCode.value).toBeNull();
  });

  it('submitNewCurrency: on failure, shows a readable error, ends the loading state, and preserves the selected currency', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleAddCurrency();
    component.newCurrencyIsoCode.setValue('EUR');
    component.submitNewCurrency();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/currencies`)
      .flush({ title: 'Conflict', status: 409, detail: 'Валюта уже добавлена.' }, { status: 409, statusText: 'Conflict' });

    expect(component.isSubmittingCurrency()).toBe(false);
    expect(component.addCurrencyError()).toBe('Валюта уже добавлена.');
    expect(component.isAddingCurrency()).toBe(true);
    expect(component.newCurrencyIsoCode.value).toBe('EUR');
  });

  it('submitNewCurrency: a second call while the first is in flight does not send a second request', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.toggleAddCurrency();
    component.newCurrencyIsoCode.setValue('EUR');
    component.submitNewCurrency();
    component.submitNewCurrency(); // guarded by isSubmittingCurrency()

    httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush({ id: 'c-eur', code: 'EUR', name: 'Евро', isActive: true });
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [{ id: 'c-eur', code: 'EUR', name: 'Евро', isActive: true }], pagination: { nextCursor: null, hasMore: false } });
  });
});
