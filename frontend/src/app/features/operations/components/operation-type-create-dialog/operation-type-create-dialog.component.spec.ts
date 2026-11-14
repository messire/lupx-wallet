import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { stubNativeDialog } from '@shared/testing/dialog.testing';
import { chooseSelectValue, selectOptionTexts } from '@shared/testing/form-controls.testing';
import { CreatedOperationType, OperationTypeCreateDialogComponent } from './operation-type-create-dialog.component';

const kinds = [
  { id: 'k-income', code: 'Income', name: 'Доход' },
  { id: 'k-expense', code: 'Expense', name: 'Расход' },
  { id: 'k-transfer', code: 'Transfer', name: 'Перевод' },
];

describe('OperationTypeCreateDialogComponent', () => {
  let fixture: ComponentFixture<OperationTypeCreateDialogComponent>;
  let httpMock: HttpTestingController;
  let restoreDialog: () => void;
  let created: CreatedOperationType[];

  beforeEach(async () => {
    restoreDialog = stubNativeDialog();
    await TestBed.configureTestingModule({
      imports: [OperationTypeCreateDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(OperationTypeCreateDialogComponent);
    created = [];
    fixture.componentInstance.created.subscribe((item) => created.push(item));
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/operation-behavior-kinds`).flush(kinds);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    fixture.destroy(); // closes the dialog while the stubbed close() is still in place
    restoreDialog();
  });

  const root = () => fixture.nativeElement as HTMLElement;

  it('offers every behavior kind except Transfer', () => {
    expect(selectOptionTexts(root(), 'behaviorKindId', () => fixture.detectChanges())).toEqual(['Доход', 'Расход']);
  });

  it('creates the type with the name and kind and emits it with the kind code', () => {
    const name = root().querySelector('input[formcontrolname="name"]') as HTMLInputElement;
    name.value = 'Зарплата';
    name.dispatchEvent(new Event('input'));
    chooseSelectValue(root(), 'behaviorKindId', 'k-income', () => fixture.detectChanges());
    (root().querySelector('button[type="submit"]') as HTMLButtonElement).click();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operation-types`);
    expect(req.request.body).toEqual({ name: 'Зарплата', behaviorKindId: 'k-income' });
    req.flush({ id: 'ot-new', name: 'Зарплата', behaviorKindId: 'k-income', isActive: true });

    expect(created).toEqual([
      { operationType: { id: 'ot-new', name: 'Зарплата', behaviorKindId: 'k-income', isActive: true }, behaviorKindCode: 'Income' },
    ]);
  });

  it('requires both fields', () => {
    (root().querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();

    expect(Array.from(root().querySelectorAll('.app-field__error')).map((e) => e.textContent?.trim())).toEqual([
      'Укажите название',
      'Выберите вид поведения',
    ]);
    httpMock.expectNone(`${environment.apiBaseUrl}/operation-types`);
  });
});
