import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { ReferenceItem } from '../../../../data-access/reference-data/reference-data-api.models';
import { stubNativeDialog } from '@shared/testing/dialog.testing';
import { WalletTypeCreateDialogComponent } from './wallet-type-create-dialog.component';

describe('WalletTypeCreateDialogComponent', () => {
  let fixture: ComponentFixture<WalletTypeCreateDialogComponent>;
  let httpMock: HttpTestingController;
  let restoreDialog: () => void;
  let created: ReferenceItem[];
  let dismissed: number;

  beforeEach(async () => {
    restoreDialog = stubNativeDialog();
    await TestBed.configureTestingModule({
      imports: [WalletTypeCreateDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(WalletTypeCreateDialogComponent);
    created = [];
    dismissed = 0;
    fixture.componentInstance.created.subscribe((item) => created.push(item));
    fixture.componentInstance.dismissed.subscribe(() => dismissed++);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    fixture.destroy(); // closes the dialog while the stubbed close() is still in place
    restoreDialog();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const nameInput = () => root().querySelector('input[formcontrolname="name"]') as HTMLInputElement;
  const submit = () => {
    (root().querySelector('button[type="submit"]') as HTMLButtonElement).click();
    fixture.detectChanges();
  };

  it('asks for the name (required) and sends nothing for a blank one', () => {
    expect(root().querySelector('dialog')?.hasAttribute('open')).toBe(true);
    expect(root().textContent).toContain('Новый тип кошелька');
    nameInput().value = '   ';
    nameInput().dispatchEvent(new Event('input'));
    submit();

    expect(root().querySelector('.app-field__error')?.textContent).toContain('Укажите название');
    httpMock.expectNone(`${environment.apiBaseUrl}/wallet-types`);
  });

  it('creates the type with the trimmed name and emits it', () => {
    nameInput().value = '  Кредитная карта ';
    nameInput().dispatchEvent(new Event('input'));
    submit();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallet-types`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ name: 'Кредитная карта' });
    req.flush({ id: 'wt-new', name: 'Кредитная карта', isActive: true });

    expect(created).toEqual([{ id: 'wt-new', name: 'Кредитная карта', isActive: true }]);
  });

  it('keeps the dialog and the typed name on a server error and shows its text', () => {
    nameInput().value = 'Наличные';
    nameInput().dispatchEvent(new Event('input'));
    submit();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallet-types`)
      .flush({ title: 'Conflict', status: 409, detail: 'Тип с таким названием уже существует.' }, { status: 409, statusText: 'Conflict' });
    fixture.detectChanges();

    expect(created).toEqual([]);
    expect(root().querySelector('p.form-error[role="alert"]')?.textContent).toContain('Тип с таким названием уже существует.');
    expect(nameInput().value).toBe('Наличные');
  });

  it('"Отмена" emits dismissed', () => {
    (Array.from(root().querySelectorAll('button')).find((b) => b.textContent?.trim() === 'Отмена') as HTMLButtonElement).click();
    expect(dismissed).toBe(1);
  });
});
