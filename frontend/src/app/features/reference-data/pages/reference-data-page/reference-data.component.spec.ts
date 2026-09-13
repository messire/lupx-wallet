import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { OperationBehaviorKind } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataComponent } from './reference-data.component';

const behaviorKinds: OperationBehaviorKind[] = [
  { id: 'bk-income', code: 'Income', name: 'Доход' },
  { id: 'bk-expense', code: 'Expense', name: 'Расход' },
  { id: 'bk-transfer', code: 'Transfer', name: 'Перевод' },
  { id: 'bk-adjustment', code: 'Adjustment', name: 'Корректировка' },
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

  function createComponent() {
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
      data: [{ id: 'c-1', code: 'USD', name: 'US Dollar', isActive: true }],
      pagination: { nextCursor: null, hasMore: false },
    });
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

    const statusCells = Array.from(compiled.querySelectorAll('.status')).map((el) => el.textContent?.trim());
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
});
