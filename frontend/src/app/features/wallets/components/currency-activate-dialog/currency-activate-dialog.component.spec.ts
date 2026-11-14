import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { stubNativeDialog } from '@shared/testing/dialog.testing';
import { CurrencyActivateDialogComponent } from './currency-activate-dialog.component';

describe('CurrencyActivateDialogComponent', () => {
  let fixture: ComponentFixture<CurrencyActivateDialogComponent>;
  let httpMock: HttpTestingController;
  let restoreDialog: () => void;
  let created: Currency[];

  beforeEach(async () => {
    restoreDialog = stubNativeDialog();
    await TestBed.configureTestingModule({
      imports: [CurrencyActivateDialogComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);
    fixture = TestBed.createComponent(CurrencyActivateDialogComponent);
    created = [];
    fixture.componentInstance.created.subscribe((item) => created.push(item));
    fixture.componentRef.setInput('activeCurrencies', [{ id: 'c-usd', code: 'USD', name: 'US Dollar' }]);
    fixture.componentRef.setInput('open', true);
    fixture.detectChanges();
    httpMock.expectOne(`${environment.apiBaseUrl}/currencies/available`).flush([
      { code: 'USD', name: 'US Dollar', isActive: true },
      { code: 'EUR', name: 'Euro', isActive: false },
      { code: 'GEL', name: 'Georgian Lari', isActive: false },
    ]);
    fixture.detectChanges();
  });

  afterEach(() => {
    httpMock.verify();
    fixture.destroy(); // closes the dialog while the stubbed close() is still in place
    restoreDialog();
  });

  it('offers only the currencies that are not active yet', () => {
    expect(fixture.componentInstance.options()).toEqual([
      { code: 'EUR', name: 'Euro' },
      { code: 'GEL', name: 'Georgian Lari' },
    ]);
  });

  it('activates the chosen currency and emits it', () => {
    fixture.componentInstance.form.controls.code.setValue('GEL');
    ((fixture.nativeElement as HTMLElement).querySelector('button[type="submit"]') as HTMLButtonElement).click();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/currencies`);
    expect(req.request.method).toBe('POST');
    expect(req.request.body).toEqual({ code: 'GEL' });
    req.flush({ id: 'c-gel', code: 'GEL', name: 'Georgian Lari' });

    expect(created).toEqual([{ id: 'c-gel', code: 'GEL', name: 'Georgian Lari' }]);
  });
});
