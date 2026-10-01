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

  const availableCurrencies = [
    { code: 'EUR', name: 'Euro', isActive: false },
    { code: 'USD', name: 'US Dollar', isActive: true },
  ];

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
      .flush({ data: [{ id: 'c-usd', code: 'USD', name: 'US Dollar' }], pagination: { nextCursor: null, hasMore: false } });
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush(availableCurrencies);

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
    const component = fixture.componentInstance;

    // The card's actions live inside its "···" menu popover, only rendered while open.
    component.toggleWalletMenu(archivedWallet.id);
    fixture.detectChanges();

    const compiled = fixture.nativeElement as HTMLElement;
    const cards = Array.from(compiled.querySelectorAll('.wallet-card'));
    const archivedCard = cards.find((card) => card.textContent?.includes('Архивный кошелек'));
    const menuItemLabels = Array.from(archivedCard!.querySelectorAll('.wallet-card__menu-item')).map((el) =>
      el.textContent?.trim(),
    );

    expect(menuItemLabels).not.toContain('Архивировать');
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

  it('uses the fallback currency-change error text (operations, including deleted) when the server sends no detail', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openChangeCurrency(secondaryWallet);
    component.currencyForm.patchValue({ currencyId: 'c-eur' });
    component.submitChangeCurrency();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/w-secondary/currency`)
      .flush(null, { status: 409, statusText: 'Conflict' });

    expect(component.currencyError()).toBe('Не удалось сменить валюту: у кошелька есть операции, включая удалённые.');
    expect(component.currencyError()).not.toContain('слепк');
  });

  it('uses the fallback delete error text (operations, including deleted) when the server sends no detail', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestConfirm(secondaryWallet.id, 'delete');
    component.confirmPendingAction();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/w-secondary`)
      .flush(null, { status: 409, statusText: 'Conflict' });

    expect(component.actionError()).toBe(
      'Не удалось удалить кошелек: возможно, он основной или у него есть операции, включая удалённые, — используйте архивирование.',
    );
    expect(component.actionError()).not.toContain('истори');
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

  it('names the consequences in the wallet delete confirmation: irreversible, balance snapshots removed, historical total may change', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    // jsdom does not implement `HTMLDialogElement.showModal`/`close` (see shared/ui/dialog spec).
    const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
    const originalShowModal = proto['showModal'];
    const originalClose = proto['close'];
    proto['showModal'] = function (this: HTMLDialogElement) {
      this.setAttribute('open', '');
    };
    proto['close'] = function (this: HTMLDialogElement) {
      this.removeAttribute('open');
    };
    try {
      component.requestConfirm(secondaryWallet.id, 'delete');
      fixture.detectChanges();

      const text = ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
      expect(text).toContain(`Кошелек «${secondaryWallet.name}» будет удален без возможности восстановления`);
      expect(text).toContain('вместе со слепками его истории баланса');
      expect(text).toContain('историческая общая сумма за прошлые даты может измениться');
    } finally {
      fixture.destroy();
      proto['showModal'] = originalShowModal;
      proto['close'] = originalClose;
    }
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

  // ---- Create form: currency chosen from the available list (loadAvailableCurrencies / submit) ----

  it('loadAvailableCurrencies: maps the available list to picker options', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    expect(component.currencyOptions()).toEqual([
      { code: 'EUR', name: 'Euro' },
      { code: 'USD', name: 'US Dollar' },
    ]);
    expect(component.availableCurrenciesLoadError()).toBeNull();
  });

  it('loadAvailableCurrencies: sets a readable error on failure and clears the loading flag', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.loadAvailableCurrencies();
    expect(component.availableCurrenciesLoading()).toBe(true);
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush('boom', { status: 500, statusText: 'Server Error' });

    expect(component.availableCurrenciesLoading()).toBe(false);
    expect(component.availableCurrenciesLoadError()).toBe('Не удалось загрузить список доступных валют.');
  });

  it('submit: sends currencyCode (not currencyId) and refreshes the active currencies afterwards', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openForm();
    component.form.patchValue({ name: 'Новый', walletTypeId: 'wt-1', currencyCode: 'EUR' });
    component.submit();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body.currencyCode).toBe('EUR');
    expect(req.request.body.currencyId).toBeUndefined();
    req.flush({ ...secondaryWallet, id: 'w-new' }, { status: 201, statusText: 'Created' });

    // The server may have activated the currency: the list that supplies the balance's code is re-read.
    httpMock.expectOne((r) => r.method === 'GET' && r.url === `${environment.apiBaseUrl}/wallets`).flush({
      data: [primaryWallet],
      pagination: { nextCursor: null, hasMore: false },
    });
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush(availableCurrencies);
  });

  it('submit: shows a currency-aware error when the server rejects the code (400)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openForm();
    component.form.patchValue({ name: 'Новый', walletTypeId: 'wt-1', currencyCode: 'XXX' });
    component.submit();

    httpMock.expectOne(`${environment.apiBaseUrl}/wallets`).flush({ status: 400 }, { status: 400, statusText: 'Bad Request' });

    expect(component.isSubmitting()).toBe(false);
    expect(component.submitError()).toContain('валюта недоступна');
  });

  describe('wallet card "···" menu', () => {
    function trigger(fixture: ReturnType<typeof createComponent>, walletId: string): HTMLButtonElement {
      return fixture.nativeElement.querySelector(`[data-wallet-menu-trigger="${walletId}"]`);
    }

    it('opens on trigger click and only one menu is open at a time', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.toggleWalletMenu(primaryWallet.id);
      expect(component.openMenuWalletId()).toBe(primaryWallet.id);

      component.toggleWalletMenu(secondaryWallet.id);
      expect(component.openMenuWalletId()).toBe(secondaryWallet.id);
    });

    it('toggling the same wallet a second time closes its menu', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.toggleWalletMenu(primaryWallet.id);
      component.toggleWalletMenu(primaryWallet.id);

      expect(component.openMenuWalletId()).toBeNull();
    });

    it('does not offer "Сделать основным" for the wallet that is already primary', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.toggleWalletMenu(primaryWallet.id);
      fixture.detectChanges();

      const compiled = fixture.nativeElement as HTMLElement;
      const cards = Array.from(compiled.querySelectorAll('.wallet-card'));
      const primaryCard = cards.find((card) => card.textContent?.includes(primaryWallet.name));
      const menuItemLabels = Array.from(primaryCard!.querySelectorAll('.wallet-card__menu-item')).map((el) =>
        el.textContent?.trim(),
      );

      expect(menuItemLabels).not.toContain('Сделать основным');
    });

    it('Escape closes the open menu and returns focus to its trigger', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(primaryWallet.id);
      fixture.detectChanges();

      component.onDocumentEscapeForMenu();

      expect(component.openMenuWalletId()).toBeNull();
      expect(document.activeElement).toBe(trigger(fixture, primaryWallet.id));
    });

    it('Escape does nothing when no menu is open', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      expect(() => component.onDocumentEscapeForMenu()).not.toThrow();
      expect(component.openMenuWalletId()).toBeNull();
    });

    it('a click outside the open menu closes it', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(primaryWallet.id);
      fixture.detectChanges();

      const outside = document.createElement('div');
      document.body.appendChild(outside);

      component.onDocumentClickForMenu({ target: outside } as unknown as MouseEvent);

      expect(component.openMenuWalletId()).toBeNull();
      outside.remove();
    });

    it('a click on the trigger itself (or inside the popover) does not close the menu via the outside-click handler', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(primaryWallet.id);
      fixture.detectChanges();

      component.onDocumentClickForMenu({ target: trigger(fixture, primaryWallet.id) } as unknown as MouseEvent);

      expect(component.openMenuWalletId()).toBe(primaryWallet.id);
    });

    it('"Сделать основным": closes the menu, returns focus to the trigger, and calls the API', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(secondaryWallet.id);
      fixture.detectChanges();

      component.selectWalletMenuAction(secondaryWallet, 'primary');

      expect(component.openMenuWalletId()).toBeNull();
      expect(document.activeElement).toBe(trigger(fixture, secondaryWallet.id));

      httpMock.expectOne(`${environment.apiBaseUrl}/wallets/${secondaryWallet.id}/set-primary`).flush({});
      httpMock
        .expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets`)
        .flush({ data: [primaryWallet, secondaryWallet], pagination: { nextCursor: null, hasMore: false } });
    });

    it('"Изменить": closes the menu, opens the edit form, and moves focus into its first field', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(secondaryWallet.id);
      fixture.detectChanges();

      component.selectWalletMenuAction(secondaryWallet, 'edit');
      fixture.detectChanges();

      expect(component.openMenuWalletId()).toBeNull();
      expect(component.editingWallet()).toEqual(secondaryWallet);

      const nameInput = fixture.nativeElement.querySelector('input[formcontrolname="name"]') as HTMLInputElement;
      expect(document.activeElement).toBe(nameInput);
    });

    it('"Сменить валюту": closes the menu, opens the currency form, and moves focus into its select', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(secondaryWallet.id);
      fixture.detectChanges();

      component.selectWalletMenuAction(secondaryWallet, 'currency');
      fixture.detectChanges();

      expect(component.openMenuWalletId()).toBeNull();
      expect(component.changingCurrencyWalletId()).toBe(secondaryWallet.id);

      const currencySelect = fixture.nativeElement.querySelector('select[formcontrolname="currencyId"]') as HTMLSelectElement;
      expect(document.activeElement).toBe(currencySelect);
    });

    it('"Удалить": closes the menu, returns focus to the trigger, and opens the confirm dialog', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      fixture.detectChanges();

      component.toggleWalletMenu(secondaryWallet.id);
      fixture.detectChanges();

      component.selectWalletMenuAction(secondaryWallet, 'delete');

      expect(component.openMenuWalletId()).toBeNull();
      expect(component.pendingConfirm()).toEqual({ walletId: secondaryWallet.id, action: 'delete' });
      expect(document.activeElement).toBe(trigger(fixture, secondaryWallet.id));
    });

    it('the trigger has an accessible name that includes the wallet name', () => {
      const fixture = createComponent();

      expect(trigger(fixture, primaryWallet.id).getAttribute('aria-label')).toBe(`Действия: ${primaryWallet.name}`);
    });
  });
});
