import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { OperationBehaviorKind } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataComponent } from './reference-data-page.component';

/**
 * jsdom does not implement `HTMLDialogElement.prototype.showModal`/`close` (see
 * `shared/ui/dialog/dialog.component.spec.ts`) — DialogComponent calls them, so the
 * currency-removal confirmation needs this polyfill.
 */
let originalShowModal: unknown;
let originalClose: unknown;

beforeAll(() => {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  originalShowModal = proto['showModal'];
  originalClose = proto['close'];
  proto['showModal'] = function (this: HTMLDialogElement) {
    this.setAttribute('open', '');
  };
  proto['close'] = function (this: HTMLDialogElement) {
    this.removeAttribute('open');
    this.dispatchEvent(new Event('close'));
  };
});

afterAll(() => {
  const proto = HTMLDialogElement.prototype as unknown as Record<string, unknown>;
  proto['showModal'] = originalShowModal;
  proto['close'] = originalClose;
});

const behaviorKinds: OperationBehaviorKind[] = [
  { id: 'bk-income', code: 'Income', name: 'Доход' },
  { id: 'bk-expense', code: 'Expense', name: 'Расход' },
  { id: 'bk-transfer', code: 'Transfer', name: 'Перевод' },
  { id: 'bk-adjustment', code: 'Adjustment', name: 'Корректировка' },
];

const availableCurrencies = [
  { code: 'EUR', name: 'Euro', isActive: false },
  { code: 'USD', name: 'US Dollar', isActive: true },
];

describe('ReferenceDataComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReferenceDataComponent, HttpClientTestingModule],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent(available = availableCurrencies) {
    const fixture = TestBed.createComponent(ReferenceDataComponent);
    fixture.detectChanges();

    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallet-types`).flush({
      data: [
        { id: 'wt-1', name: 'Наличные', isActive: true },
        { id: 'wt-2', name: 'Устаревший', isActive: false },
      ],
      pagination: { nextCursor: null, hasMore: false },
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/operation-types`).flush({
      data: [{ id: 'ot-1', name: 'Зарплата', behaviorKindId: 'bk-income', isActive: true }],
      pagination: { nextCursor: null, hasMore: false },
    });
    httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`).flush({
      data: [{ id: 'c-1', code: 'USD', name: 'US Dollar' }],
      pagination: { nextCursor: null, hasMore: false },
    });
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush(available);
    httpMock.expectOne(`${environment.apiBaseUrl}/operation-behavior-kinds`).flush(behaviorKinds);

    fixture.detectChanges();
    return fixture;
  }

  it('should create and load all three reference lists', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    expect(component.walletTypes().length).toBe(2);
    expect(component.operationTypes().length).toBe(1);
    expect(component.currencies().length).toBe(1);
    expect(component.isLoading()).toBe(false);
  });

  it('excludes the system Transfer behavior kind from creatable options', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    const codes = component.creatableBehaviorKinds().map((kind) => kind.code);
    expect(codes).toEqual(['Income', 'Expense', 'Adjustment']);
    expect(codes).not.toContain('Transfer');
  });

  it('renders active/inactive status for wallet types', () => {
    const fixture = createComponent();
    const compiled = fixture.nativeElement as HTMLElement;

    const statusCells = Array.from(compiled.querySelectorAll('app-badge')).map((el) => el.textContent?.trim());
    expect(statusCells).toContain('Активен');
    expect(statusCells).toContain('Деактивирован');
  });

  it('reloads wallet types after a successful deactivation', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.deactivateWalletType({ id: 'wt-1', name: 'Наличные', isActive: true });

    const deactivateReq = httpMock.expectOne(`${environment.apiBaseUrl}/wallet-types/wt-1/deactivate`);
    expect(deactivateReq.request.method).toBe('POST');
    deactivateReq.flush({ id: 'wt-1', name: 'Наличные', isActive: false });

    const reloadReq = httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallet-types`);
    reloadReq.flush({
      data: [{ id: 'wt-1', name: 'Наличные', isActive: false }],
      pagination: { nextCursor: null, hasMore: false },
    });

    expect(component.walletTypes()[0].isActive).toBe(false);
  });

  it('shows a readable error message when deleting a used wallet type returns 409', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.deleteWalletType({ id: 'wt-1', name: 'Наличные', isActive: true });

    const deleteReq = httpMock.expectOne(`${environment.apiBaseUrl}/wallet-types/wt-1`);
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(
      { title: 'Conflict', status: 409, detail: 'Тип уже используется хотя бы одним кошельком — используйте деактивацию.' },
      { status: 409, statusText: 'Conflict' },
    );

    expect(component.actionError()).toBe('Тип уже используется хотя бы одним кошельком — используйте деактивацию.');
  });

  // ---- Currencies tab: active list + add from available + remove from active ----

  it('offers only available currencies that are not active yet', () => {
    const fixture = createComponent();

    expect(fixture.componentInstance.addableCurrencies()).toEqual([{ code: 'EUR', name: 'Euro' }]);
  });

  it('shows the empty-state hint instead of the add form when no currencies are available', () => {
    const fixture = createComponent([]);
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('app-currency-select')).toBeNull();
    expect(compiled.textContent).toContain('нажмите «Обновить сейчас» на экране курсов');
  });

  it('adds the chosen currency by code only and reloads both lists', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.currencyForm.patchValue({ code: 'EUR' });
    component.submitCurrency();

    const createReq = httpMock.expectOne(`${environment.apiBaseUrl}/currencies`);
    expect(createReq.request.method).toBe('POST');
    expect(createReq.request.body).toEqual({ code: 'EUR' });
    createReq.flush({ id: 'c-2', code: 'EUR', name: 'Euro' }, { status: 201, statusText: 'Created' });

    httpMock
      .expectOne((req) => req.method === 'GET' && req.url === `${environment.apiBaseUrl}/currencies`)
      .flush({
        data: [
          { id: 'c-1', code: 'USD', name: 'US Dollar' },
          { id: 'c-2', code: 'EUR', name: 'Euro' },
        ],
        pagination: { nextCursor: null, hasMore: false },
      });
    httpMock
      .expectOne(`${environment.apiBaseUrl}/currencies/available`)
      .flush(availableCurrencies.map((currency) => ({ ...currency, isActive: true })));

    expect(component.currencies().length).toBe(2);
    expect(component.addableCurrencies()).toEqual([]);
    expect(component.currencyForm.getRawValue().code).toBe('');
  });

  it('shows the server message when adding an unavailable currency fails with 400', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.currencyForm.patchValue({ code: 'EUR' });
    component.submitCurrency();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/currencies`)
      .flush({ title: 'Bad Request', status: 400, detail: 'Валюта недоступна.' }, { status: 400, statusText: 'Bad Request' });

    expect(component.actionError()).toBe('Валюта недоступна.');
    expect(component.isSubmittingCurrency()).toBe(false);
  });

  it('removing a currency asks for confirmation first and sends no request until confirmed', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
    expect(component.pendingCurrencyRemoval()?.code).toBe('USD');

    component.cancelCurrencyRemoval();
    expect(component.pendingCurrencyRemoval()).toBeNull();
    httpMock.expectNone(`${environment.apiBaseUrl}/currencies/c-1`);
  });

  it('confirmed removal deletes the currency and reloads both lists', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
    component.confirmCurrencyRemoval();

    const deleteReq = httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`);
    expect(deleteReq.request.method).toBe('DELETE');
    deleteReq.flush(null, { status: 204, statusText: 'No Content' });

    httpMock
      .expectOne((req) => req.method === 'GET' && req.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    httpMock
      .expectOne(`${environment.apiBaseUrl}/currencies/available`)
      .flush([
        { code: 'EUR', name: 'Euro', isActive: false },
        { code: 'USD', name: 'US Dollar', isActive: false },
      ]);

    expect(component.currencies()).toEqual([]);
    expect(component.pendingCurrencyRemoval()).toBeNull();
    expect(component.addableCurrencies().length).toBe(2);
  });

  it('shows the in-use message when removing a used currency fails with 409', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
    component.confirmCurrencyRemoval();

    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`).flush(null, { status: 409, statusText: 'Conflict' });

    expect(component.actionError()).toBe('Валюта используется кошельком или операцией (включая удалённые).');
  });

  // ---- Currencies tab: rendered behaviour (combobox, empty states, confirmation dialog, error texts) ----

  describe('currencies tab (DOM)', () => {
    const currencyListUrl = (req: { method: string; url: string }) =>
      req.method === 'GET' && req.url === `${environment.apiBaseUrl}/currencies`;
    const emptyPage = { data: [], pagination: { nextCursor: null, hasMore: false } };

    function openCombobox(fixture: ReturnType<typeof createComponent>): HTMLInputElement {
      const combobox = (fixture.nativeElement as HTMLElement).querySelector('app-currency-select input') as HTMLInputElement;
      combobox.dispatchEvent(new Event('focus'));
      fixture.detectChanges();
      return combobox;
    }

    const optionTexts = (fixture: ReturnType<typeof createComponent>) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('app-currency-select [role="option"]')).map((el) =>
        el.textContent?.replace(/\s+/g, ' ').trim(),
      );

    const currencyTableCodes = (fixture: ReturnType<typeof createComponent>) =>
      Array.from((fixture.nativeElement as HTMLElement).querySelectorAll('table.ref-table'))
        .filter((table) => table.querySelector('th')?.textContent?.trim() === 'Код')
        .flatMap((table) => Array.from(table.querySelectorAll('tbody td:first-child')).map((td) => td.textContent?.trim()));

    function flushReload(currencies: Array<{ id: string; code: string; name: string }>, available: unknown[]) {
      httpMock.expectOne(currencyListUrl).flush({ data: currencies, pagination: { nextCursor: null, hasMore: false } });
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush(available);
    }

    it('the add combobox lists available minus active currencies only', () => {
      const fixture = createComponent();
      openCombobox(fixture);

      expect(optionTexts(fixture)).toEqual(['EUR — Euro']);
    });

    it('the add combobox is labelled "Добавить в активные" for assistive technology', () => {
      const fixture = createComponent();
      const root = fixture.nativeElement as HTMLElement;
      const label = root.querySelector('label[for="currencyQuickAddControl"]');
      const combobox = root.querySelector('app-currency-select input') as HTMLInputElement;

      expect(label?.textContent?.trim()).toBe('Добавить в активные');
      expect(combobox.id).toBe('currencyQuickAddControl');
      // The id must be unique: a duplicate on the <app-currency-select> host would capture the label
      // (it points at the first element with that id) and leave the combobox without an accessible name.
      expect(root.querySelectorAll('#currencyQuickAddControl').length).toBe(1);
      expect(root.querySelector('app-currency-select')!.hasAttribute('id')).toBe(false);
    });

    it('says all available currencies are already added when none is left to offer (the form stays)', () => {
      const fixture = createComponent([
        { code: 'EUR', name: 'Euro', isActive: true },
        { code: 'USD', name: 'US Dollar', isActive: true },
      ]);
      openCombobox(fixture);
      const root = fixture.nativeElement as HTMLElement;

      expect(fixture.componentInstance.addableCurrencies()).toEqual([]);
      expect(root.querySelector('app-currency-select')).not.toBeNull();
      expect(root.querySelector('app-currency-select [role="listbox"]')?.textContent).toContain('Все доступные валюты уже добавлены');
      expect(root.textContent).not.toContain('Список доступных валют пуст');
    });

    it('with an empty available list shows the hint and no add form, but still lists the active currencies', () => {
      const fixture = createComponent([]);
      const root = fixture.nativeElement as HTMLElement;

      expect(root.querySelector('app-currency-select')).toBeNull();
      expect(root.textContent).toContain('Список доступных валют пуст');
      expect(currencyTableCodes(fixture)).toEqual(['USD']);
    });

    it('the Добавить button is disabled until a currency is picked; a pick from the list is sent as code only', () => {
      const fixture = createComponent();
      const root = fixture.nativeElement as HTMLElement;
      const submit = root.querySelector('app-currency-select')!.closest('form')!.querySelector('app-button button') as HTMLButtonElement;
      expect(fixture.componentInstance.currencyForm.invalid).toBe(true);
      expect(submit.disabled).toBe(true);

      const combobox = openCombobox(fixture);
      combobox.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', cancelable: true }));
      fixture.detectChanges();
      expect(fixture.componentInstance.currencyForm.getRawValue().code).toBe('EUR');
      expect(submit.disabled).toBe(false);

      fixture.componentInstance.submitCurrency();
      const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies`);
      expect(req.request.body).toEqual({ code: 'EUR' });
      req.flush({ id: 'c-2', code: 'EUR', name: 'Euro' }, { status: 201, statusText: 'Created' });
      flushReload(
        [
          { id: 'c-1', code: 'USD', name: 'US Dollar' },
          { id: 'c-2', code: 'EUR', name: 'Euro' },
        ],
        [
          { code: 'EUR', name: 'Euro', isActive: true },
          { code: 'USD', name: 'US Dollar', isActive: true },
        ],
      );
      fixture.detectChanges();

      expect(currencyTableCodes(fixture)).toEqual(['USD', 'EUR']);
    });

    it('does not submit twice while the first add request is in flight', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;

      component.currencyForm.patchValue({ code: 'EUR' });
      component.submitCurrency();
      component.submitCurrency();

      const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies`);
      expect(component.isSubmittingCurrency()).toBe(true);
      req.flush({ id: 'c-2', code: 'EUR', name: 'Euro' }, { status: 201, statusText: 'Created' });
      flushReload([{ id: 'c-2', code: 'EUR', name: 'Euro' }], availableCurrencies);
    });

    it('does not submit an empty form', () => {
      const fixture = createComponent();

      fixture.componentInstance.submitCurrency();

      httpMock.expectNone(`${environment.apiBaseUrl}/currencies`);
    });

    it('after an add both lists are re-read (active with limit and without includeInactive, plus available)', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.currencyForm.patchValue({ code: 'EUR' });
      component.submitCurrency();
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush({ id: 'c-2', code: 'EUR', name: 'Euro' }, { status: 201, statusText: 'Created' });

      // Both GETs are outstanding at once.
      const listReq = httpMock.expectOne(currencyListUrl);
      const availableReq = httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`);
      expect(listReq.request.params.get('limit')).toBe('100');
      expect(listReq.request.params.has('includeInactive')).toBe(false);
      listReq.flush({ data: [{ id: 'c-1', code: 'USD', name: 'US Dollar' }], pagination: { nextCursor: null, hasMore: false } });
      availableReq.flush(availableCurrencies);
    });

    it('shows an error when the re-read after an add fails, keeping the lists already on screen', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.currencyForm.patchValue({ code: 'EUR' });
      component.submitCurrency();
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush({ id: 'c-2', code: 'EUR', name: 'Euro' }, { status: 201, statusText: 'Created' });

      httpMock.expectOne(currencyListUrl).flush('boom', { status: 500, statusText: 'Server Error' });
      // forkJoin cancels the sibling request on the first error.
      httpMock.match(`${environment.apiBaseUrl}/currencies/available`);

      expect(component.actionError()).toBe('Не удалось обновить список валют.');
      expect(component.currencies().length).toBe(1);
    });

    it('409 on add without a server message falls back to "already added"', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.currencyForm.patchValue({ code: 'EUR' });
      component.submitCurrency();

      httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush(null, { status: 409, statusText: 'Conflict' });

      expect(component.actionError()).toBe('Валюта уже добавлена в активные.');
      expect(component.isSubmittingCurrency()).toBe(false);
    });

    it('400 on add without a server message falls back to "currency unavailable"', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.currencyForm.patchValue({ code: 'EUR' });
      component.submitCurrency();

      httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush(null, { status: 400, statusText: 'Bad Request' });

      expect(component.actionError()).toBe('Валюта недоступна: её нет в списке сервиса курсов.');
    });

    it('a 500 on add shows the generic message and keeps the chosen code', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.currencyForm.patchValue({ code: 'EUR' });
      component.submitCurrency();

      httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush('boom', { status: 500, statusText: 'Server Error' });

      expect(component.actionError()).toBe('Не удалось добавить валюту.');
      expect(component.currencyForm.getRawValue().code).toBe('EUR');
    });

    it('an error from a previous action is cleared when a new add starts', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.actionError.set('старая ошибка');
      component.currencyForm.patchValue({ code: 'EUR' });

      component.submitCurrency();

      expect(component.actionError()).toBeNull();
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies`).flush({ id: 'c-2', code: 'EUR', name: 'Euro' });
      flushReload([{ id: 'c-2', code: 'EUR', name: 'Euro' }], availableCurrencies);
    });

    it('the active currencies table has columns Код/Название and only "Убрать из активных" as the action (no deactivation, no status)', () => {
      const fixture = createComponent();
      const root = fixture.nativeElement as HTMLElement;
      const currencyTable = Array.from(root.querySelectorAll('table.ref-table')).find((table) =>
        table.textContent?.includes('US Dollar'),
      )!;

      expect(Array.from(currencyTable.querySelectorAll('th')).map((th) => th.textContent?.trim())).toEqual(['Код', 'Название', '']);
      expect(Array.from(currencyTable.querySelectorAll('tbody button')).map((b) => b.textContent?.trim())).toEqual(['Убрать из активных']);
      expect(currencyTable.querySelector('app-badge')).toBeNull();
      expect(currencyTable.textContent).not.toContain('Деактивировать');
    });

    it('shows the empty state for active currencies when there are none', () => {
      const fixture = TestBed.createComponent(ReferenceDataComponent);
      fixture.detectChanges();
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallet-types`).flush(emptyPage);
      httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/operation-types`).flush(emptyPage);
      httpMock.expectOne(currencyListUrl).flush(emptyPage);
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush(availableCurrencies);
      httpMock.expectOne(`${environment.apiBaseUrl}/operation-behavior-kinds`).flush(behaviorKinds);
      fixture.detectChanges();

      expect((fixture.nativeElement as HTMLElement).textContent).toContain('Активных валют пока нет');
    });

    it('clicking "Убрать из активных" opens the confirmation dialog naming the currency and sends nothing yet', () => {
      const fixture = createComponent();
      const root = fixture.nativeElement as HTMLElement;
      const removeButton = Array.from(root.querySelectorAll('table.ref-table button')).find(
        (b) => b.textContent?.trim() === 'Убрать из активных',
      ) as HTMLButtonElement;

      removeButton.click();
      fixture.detectChanges();

      expect(fixture.componentInstance.pendingCurrencyRemoval()?.code).toBe('USD');
      const dialog = root.querySelector('dialog') as HTMLDialogElement;
      expect(dialog.textContent).toContain('Убрать валюту из активных');
      expect(dialog.textContent).toContain('Валюта USD будет убрана из активных вместе с её курсами');
      expect(dialog.textContent).toContain('включая удалённые');
      httpMock.expectNone(`${environment.apiBaseUrl}/currencies/c-1`);
    });

    it('"Отмена" in the dialog sends nothing; "Убрать" deletes the currency and re-reads both lists', () => {
      const fixture = createComponent();
      const root = fixture.nativeElement as HTMLElement;
      const component = fixture.componentInstance;
      const dialogButton = (label: string) =>
        Array.from(root.querySelectorAll('dialog button')).find((b) => b.textContent?.trim() === label) as HTMLButtonElement;

      component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
      fixture.detectChanges();
      dialogButton('Отмена').click();
      fixture.detectChanges();
      expect(component.pendingCurrencyRemoval()).toBeNull();
      httpMock.expectNone(`${environment.apiBaseUrl}/currencies/c-1`);

      component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
      fixture.detectChanges();
      dialogButton('Убрать').click();
      fixture.detectChanges();

      const deleteReq = httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`);
      expect(deleteReq.request.method).toBe('DELETE');
      deleteReq.flush(null, { status: 204, statusText: 'No Content' });
      flushReload([], availableCurrencies.map((currency) => ({ ...currency, isActive: false })));
      expect(component.currencies()).toEqual([]);
    });

    it('confirming a removal without a pending currency sends nothing', () => {
      const fixture = createComponent();

      fixture.componentInstance.confirmCurrencyRemoval();

      httpMock.expectNone((req) => req.method === 'DELETE');
    });

    it('409 on removal shows the server message and keeps the currency in the list', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
      component.confirmCurrencyRemoval();

      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`).flush(
        { title: 'Conflict', status: 409, detail: 'Валюта используется кошельком или операцией (включая удалённые).' },
        { status: 409, statusText: 'Conflict' },
      );

      expect(component.actionError()).toBe('Валюта используется кошельком или операцией (включая удалённые).');
      expect(component.currencies().length).toBe(1);
      expect(component.pendingCurrencyRemoval()).toBeNull();
    });

    it('the 409 removal error is rendered as an alert without the word "деактивация"', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
      component.confirmCurrencyRemoval();
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`).flush(null, { status: 409, statusText: 'Conflict' });
      fixture.detectChanges();

      const alert = (fixture.nativeElement as HTMLElement).querySelector('[role="alert"]');
      expect(alert?.textContent).toContain('Валюта используется кошельком или операцией (включая удалённые).');
      expect(alert?.textContent?.toLowerCase()).not.toContain('деактивац');
    });

    it('a non-409 failure on removal shows the generic message', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });
      component.confirmCurrencyRemoval();

      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/c-1`).flush('boom', { status: 500, statusText: 'Server Error' });

      expect(component.actionError()).toBe('Не удалось убрать валюту из активных.');
    });

    it('requesting a removal clears an earlier action error', () => {
      const fixture = createComponent();
      const component = fixture.componentInstance;
      component.actionError.set('старая ошибка');

      component.requestCurrencyRemoval({ id: 'c-1', code: 'USD', name: 'US Dollar' });

      expect(component.actionError()).toBeNull();
    });

    it('the initial load requests active currencies with a limit and without includeInactive, and the available list', () => {
      const fixture = TestBed.createComponent(ReferenceDataComponent);
      fixture.detectChanges();

      const currencyReq = httpMock.expectOne(currencyListUrl);
      expect(currencyReq.request.params.has('includeInactive')).toBe(false);
      expect(currencyReq.request.params.get('limit')).toBe('100');
      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`);

      // Wallet/operation types keep includeInactive (they do have an inactive state).
      const walletTypesReq = httpMock.expectOne((req) => req.url === `${environment.apiBaseUrl}/wallet-types`);
      expect(walletTypesReq.request.params.get('includeInactive')).toBe('true');
      httpMock.match(() => true);
    });

    it('shows the load error with a retry when one of the initial requests fails', () => {
      const fixture = TestBed.createComponent(ReferenceDataComponent);
      fixture.detectChanges();

      httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush('boom', { status: 500, statusText: 'Server Error' });
      httpMock.match(() => true);
      fixture.detectChanges();

      expect(fixture.componentInstance.loadError()).toBe('Не удалось загрузить справочники.');
      expect((fixture.nativeElement as HTMLElement).querySelector('app-error-state')).not.toBeNull();
    });
  });
});
