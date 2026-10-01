import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { OperationBehaviorKind } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataComponent } from './reference-data-page.component';

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
});
