import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { By } from '@angular/platform-browser';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { Transfer } from '../../../../data-access/transfers/transfers-api.models';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { TransferFormComponent } from '../../components/transfer-form/transfer-form.component';
import { TransfersComponent } from './transfers-page.component';

const walletUsd1: Wallet = {
  id: 'wallet-1',
  name: 'Наличные USD',
  walletTypeId: 'wt-1',
  purposeDescription: null,
  currencyId: 'c-usd',
  initialBalance: { amount: '0', currencyId: 'c-usd' },
  accountingStartDate: '2026-01-01',
  currentBalance: { amount: '500', currencyId: 'c-usd' },
  includeInTotal: true,
  isPrimary: true,
  isArchived: false,
  displayOrder: 0,
  color: null,
  icon: null,
  createdAt: '2026-01-01T00:00:00Z',
  updatedAt: '2026-01-01T00:00:00Z',
};

const walletUsd2: Wallet = {
  ...walletUsd1,
  id: 'wallet-2',
  name: 'Карта USD',
  isPrimary: false,
  accountingStartDate: '2026-02-01',
};

const currencies: Currency[] = [{ id: 'c-usd', code: 'USD', name: 'US Dollar' }];

const transfer1: Transfer = {
  id: 't-1',
  sourceWalletId: 'wallet-1',
  targetWalletId: 'wallet-2',
  sourceOperationId: 'op-1',
  targetOperationId: 'op-2',
  amount: { amount: '100.00', currencyId: 'c-usd' },
  transferDate: '2026-09-10',
  createdAt: '2026-09-10T00:00:00Z',
  isDeleted: false,
  deletedAt: null,
};

/**
 * jsdom does not implement `HTMLDialogElement.prototype.showModal`/`close` (see
 * `shared/ui/dialog/dialog.component.spec.ts` for details) — DialogComponent calls
 * them, so any test that opens a dialog needs this polyfill.
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

function compiledText(fixture: ComponentFixture<TransfersComponent>): string {
  return (fixture.nativeElement as HTMLElement).textContent ?? '';
}

describe('TransfersComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TransfersComponent, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent(transfers: Transfer[] = [transfer1]): ComponentFixture<TransfersComponent> {
    const fixture = TestBed.createComponent(TransfersComponent);
    fixture.detectChanges();

    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/wallets`)
      .flush({ data: [walletUsd1, walletUsd2], pagination: { nextCursor: null, hasMore: false } });
    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/currencies`)
      .flush({ data: currencies, pagination: { nextCursor: null, hasMore: false } });
    httpMock
      .expectOne((req) => req.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: transfers, pagination: { nextCursor: null, hasMore: false } });

    fixture.detectChanges();
    return fixture;
  }

  function transferFormInstance(fixture: ComponentFixture<TransfersComponent>): TransferFormComponent {
    const debugEl = fixture.debugElement.query(By.directive(TransferFormComponent));
    expect(debugEl).toBeTruthy();
    return debugEl.componentInstance as TransferFormComponent;
  }

  // ---- A4/CRITICAL regression: the form dialog recreates <app-transfer-form> on every open ----

  it('recreates the transfer form component on every open, so it never retains input from a previous open', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const firstInstance = transferFormInstance(fixture);
    firstInstance.form.controls.amount.setValue('999.99');
    fixture.detectChanges();
    expect(firstInstance.form.controls.amount.value).toBe('999.99');

    component.cancelForm();
    fixture.detectChanges();
    expect(fixture.debugElement.query(By.directive(TransferFormComponent))).toBeNull();

    component.openCreateForm();
    fixture.detectChanges();

    const secondInstance = transferFormInstance(fixture);
    expect(secondInstance).not.toBe(firstInstance);
    expect(secondInstance.form.controls.amount.value).toBe('');
  });

  // ---- A9: isSubmitting blocks the fields, not just a second submit ----

  it('disables the transfer form fields while a save request is in flight', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const formInstance = transferFormInstance(fixture);
    expect(formInstance.form.disabled).toBe(false);

    component.saveTransfer({ sourceWalletId: 'wallet-1', targetWalletId: 'wallet-2', amount: '10.00', transferDate: '2026-09-10' });
    fixture.detectChanges();
    fixture.detectChanges();

    expect(component.isSubmitting()).toBe(true);
    expect(formInstance.form.disabled).toBe(true);

    httpMock.expectOne(`${environment.apiBaseUrl}/transfers`).flush(transfer1);
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`).flush({ data: [transfer1], pagination: { nextCursor: null, hasMore: false } });
  });

  it('a second saveTransfer call while the first is in flight does not send a second request', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const request = { sourceWalletId: 'wallet-1', targetWalletId: 'wallet-2', amount: '10.00', transferDate: '2026-09-10' };
    component.saveTransfer(request);
    component.saveTransfer(request); // guarded by isSubmitting()

    // If a second request had been sent, expectOne below would fail with "multiple matches".
    httpMock.expectOne(`${environment.apiBaseUrl}/transfers`).flush(transfer1);
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`).flush({ data: [transfer1], pagination: { nextCursor: null, hasMore: false } });
  });

  // ---- A10: a save error keeps the dialog open and the input, and shows the error ----

  it('keeps the form dialog open and the entered values when saving fails (400/409)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const formInstance = transferFormInstance(fixture);
    formInstance.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-2',
      amount: '250.00',
      transferDate: '2026-09-10',
    });

    component.saveTransfer({ sourceWalletId: 'wallet-1', targetWalletId: 'wallet-2', amount: '250.00', transferDate: '2026-09-10' });

    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers`)
      .flush(
        { title: 'Conflict', status: 409, detail: 'Кошельки имеют разные валюты.' },
        { status: 409, statusText: 'Conflict' },
      );
    fixture.detectChanges();

    expect(component.isFormOpen()).toBe(true);
    expect(component.submitError()).toBe('Кошельки имеют разные валюты.');
    expect(formInstance.form.controls.amount.value).toBe('250.00');

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Кошельки имеют разные валюты.');
  });

  // ---- A11: discard confirmation only for a dirty form ----

  it('opens the discard-confirm dialog when closing a modified form', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const formInstance = transferFormInstance(fixture);
    formInstance.form.markAsDirty();

    component.requestCloseForm();

    expect(component.discardConfirmOpen()).toBe(true);
    expect(component.isFormOpen()).toBe(true);
  });

  it('closes the form immediately, without a confirmation, when it was never touched', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    component.requestCloseForm();

    expect(component.discardConfirmOpen()).toBe(false);
    expect(component.isFormOpen()).toBe(false);
  });

  it('Escape (dismissRequested) on a modified form prevents the native close and opens the discard-confirm dialog', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const formInstance = transferFormInstance(fixture);
    formInstance.form.markAsDirty();

    const event = new Event('cancel', { cancelable: true });
    const preventDefaultSpy = vi.spyOn(event, 'preventDefault');
    component.onFormDismissRequested(event);

    expect(preventDefaultSpy).toHaveBeenCalled();
    expect(component.discardConfirmOpen()).toBe(true);
    expect(component.isFormOpen()).toBe(true);
  });

  it('confirming discard closes the form and clears the confirmation state', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openCreateForm();
    fixture.detectChanges();

    const formInstance = transferFormInstance(fixture);
    formInstance.form.markAsDirty();
    component.requestCloseForm();

    component.confirmDiscard();

    expect(component.discardConfirmOpen()).toBe(false);
    expect(component.isFormOpen()).toBe(false);
  });

  // ---- A13/A14: "···" -> actions dialog -> delete -> confirm dialog with both wallets/amount/date ----

  it('opens the delete-confirm dialog with both wallets, the amount+currency, and the date after "Удалить перевод"', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.openRowActions(transfer1);
    component.deleteFromRowActions(transfer1);
    component.onRowActionsDialogClosed();
    fixture.detectChanges();

    expect(component.pendingDeleteTransfer()).toEqual(transfer1);

    const compiled = fixture.nativeElement as HTMLElement;
    const text = compiled.textContent ?? '';
    expect(text).toContain('Наличные USD');
    expect(text).toContain('Карта USD');
    expect(text).toContain('100');
    expect(text).toContain('USD');
    expect(text).toContain('2026-09-10');
  });

  // ---- A14: delete failure (409, already deleted) shown inside the delete dialog, not lost ----

  it('shows the delete error inside the delete dialog and keeps it open on a 409 (already deleted)', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(transfer1);
    component.confirmDeleteTransfer();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(
      { title: 'Conflict', status: 409, detail: 'Перевод уже удалён.' },
      { status: 409, statusText: 'Conflict' },
    );
    // 409 means the list on screen is stale (changed in another tab) - it is reloaded.
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: [{ ...transfer1, isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' }], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    expect(component.pendingDeleteTransfer()).toEqual(transfer1);
    expect(component.deleteError()).toBe('Перевод уже удалён.');
    expect(compiledText(fixture)).toContain('Удалить перевод?');

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Перевод уже удалён');
  });

  // ---- MAJOR-2: Escape/dismiss on the delete-confirm dialog while a DELETE is in flight ----

  it('Escape (dismissRequested) on the delete-confirm dialog while deleting prevents the native close', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(transfer1);
    component.confirmDeleteTransfer();
    fixture.detectChanges();

    expect(component.isDeleting()).toBe(true);

    const event = new Event('cancel', { cancelable: true });
    const preventDefaultSpy = vi.spyOn(event, 'preventDefault');
    component.onDeleteDismissRequested(event);

    expect(preventDefaultSpy).toHaveBeenCalled();
    // The dialog stays associated with the same transfer — it was not dismissed.
    expect(component.pendingDeleteTransfer()).toEqual(transfer1);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}`);
    req.flush(
      { title: 'Conflict', status: 409, detail: 'Перевод уже удалён.' },
      { status: 409, statusText: 'Conflict' },
    );
    // 409 means the list on screen is stale (changed in another tab) - it is reloaded.
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: [transfer1], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    // The request's outcome (the 409) is still visible — nothing was silently closed.
    expect(component.pendingDeleteTransfer()).toEqual(transfer1);
    expect(component.deleteError()).toBe('Перевод уже удалён.');
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Перевод уже удалён.');
  });

  it('does not prevent the close, and clears the pending transfer, when dismissRequested fires while not deleting', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(transfer1);
    fixture.detectChanges();

    const event = new Event('cancel', { cancelable: true });
    const preventDefaultSpy = vi.spyOn(event, 'preventDefault');
    component.onDeleteDismissRequested(event);

    expect(preventDefaultSpy).not.toHaveBeenCalled();
    expect(component.pendingDeleteTransfer()).toBeNull();
  });

  // ---- MINOR-3: the row's "···" trigger has a unique, descriptive aria-label ----

  it('gives the row-actions trigger a unique aria-label with the transfer\'s source/target/amount/date', () => {
    const fixture = createComponent([transfer1]);
    const compiled = fixture.nativeElement as HTMLElement;
    const trigger = compiled.querySelector('td.actions button') as HTMLButtonElement;

    const label = trigger.getAttribute('aria-label') ?? '';
    expect(label).toContain('Наличные USD');
    expect(label).toContain('Карта USD');
    expect(label).toContain('2026-09-10');
    expect(label).not.toBe('Действия с переводом');
  });

  // ---- ADR-0002: any transfer can be deleted (no past-date rule); a deleted one can be restored ----

  it('offers deletion (with no past-date warning) for an active transfer and restore for a deleted one', () => {
    const deletedTransfer: Transfer = { ...transfer1, id: 't-deleted', isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([transfer1, deletedTransfer]);
    const component = fixture.componentInstance;

    component.openRowActions(transfer1); // 2026-09-10 — in the past, still deletable
    fixture.detectChanges();
    const actionsText = () => (fixture.nativeElement as HTMLElement).querySelector('.row-actions-list')?.textContent ?? '';
    let text = actionsText();
    expect(text).toContain('Удалить перевод');
    expect(text).not.toContain('удалить нельзя');
    expect(text).not.toContain('Восстановить перевод');

    component.closeRowActions();
    component.openRowActions(deletedTransfer);
    fixture.detectChanges();
    text = actionsText();
    expect(text).toContain('Восстановить перевод');
    expect(text).not.toContain('Удалить перевод');
  });

  it('marks a deleted transfer row with is-deleted and the "Удалён" badge', () => {
    const deletedTransfer: Transfer = { ...transfer1, id: 't-deleted', isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([transfer1, deletedTransfer]);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    expect(rows[0].classList.contains('is-deleted')).toBe(false);
    expect(rows[1].classList.contains('is-deleted')).toBe(true);
    expect(rows[1].textContent).toContain('Удалён');
  });

  it('restores a deleted transfer without confirmation, reloads the list and shows a toast', () => {
    const deletedTransfer: Transfer = { ...transfer1, isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([deletedTransfer]);
    const component = fixture.componentInstance;

    component.restoreFromRowActions(deletedTransfer);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}/restore`);
    expect(req.request.method).toBe('POST');
    req.flush(transfer1);

    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`).flush({
      data: [transfer1],
      pagination: { nextCursor: null, hasMore: false },
    });
    expect(component.successMessage()).toBe('Перевод восстановлен');
  });

  it('sets restoreError (server detail) and shows it as an error toast when restoring a transfer fails', () => {
    const deletedTransfer: Transfer = { ...transfer1, isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([deletedTransfer]);
    const component = fixture.componentInstance;

    component.restoreFromRowActions(deletedTransfer);

    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}/restore`)
      .flush(
        { title: 'Conflict', status: 409, detail: 'Перевод не удалён.' },
        { status: 409, statusText: 'Conflict' },
      );
    // 409 means the list on screen is stale (changed in another tab) - it is reloaded.
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: [transfer1], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    expect(component.restoreError()).toBe('Перевод не удалён.');
    expect(component.successMessage()).toBeNull();
    expect(component.isRestoring()).toBe(false);
    const toast = (fixture.nativeElement as HTMLElement).querySelector('app-toast .app-toast') as HTMLElement;
    expect(toast.textContent).toContain('Перевод не удалён.');
    expect(toast.getAttribute('role')).toBe('alert');
  });

  it('does not reload the list when a delete or restore fails with a non-conflict error', () => {
    const deletedTransfer: Transfer = { ...transfer1, id: 't-deleted', isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([transfer1, deletedTransfer]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(transfer1);
    component.confirmDeleteTransfer();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    component.restoreFromRowActions(deletedTransfer);
    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers/${deletedTransfer.id}/restore`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/transfers`);
  });

  it('falls back to a generic restoreError text when the server sends no problem details', () => {
    const deletedTransfer: Transfer = { ...transfer1, isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([deletedTransfer]);
    const component = fixture.componentInstance;

    component.restoreFromRowActions(deletedTransfer);

    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}/restore`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.restoreError()).toBe('Не удалось восстановить перевод.');
  });

  it('does not send a second restore request while the first is in flight, and clears restoreError on a new attempt', () => {
    const deletedTransfer: Transfer = { ...transfer1, isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' };
    const fixture = createComponent([deletedTransfer]);
    const component = fixture.componentInstance;

    component.restoreFromRowActions(deletedTransfer);
    component.restoreFromRowActions(deletedTransfer); // guarded by isRestoring()
    expect(component.isRestoring()).toBe(true);

    httpMock
      .expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}/restore`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    expect(component.restoreError()).not.toBeNull();

    component.restoreFromRowActions(deletedTransfer);
    expect(component.restoreError()).toBeNull();
    httpMock.expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}/restore`).flush(transfer1);
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: [transfer1], pagination: { nextCursor: null, hasMore: false } });
  });

  it('shows the new delete-confirm text: record stays in the list, is excluded from both balances, can be restored', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(transfer1); // 2026-09-10 — in the past, still deletable
    fixture.detectChanges();

    const text = ((fixture.nativeElement as HTMLElement).textContent ?? '').replace(/\s+/g, ' ');
    expect(text).toContain('Запись останется в списке, исключится из расчётов балансов обоих кошельков, её можно восстановить.');
    expect(text).not.toContain('без возможности восстановления');
    expect(text).not.toContain('Отменить нельзя');
    expect(text).not.toContain('удалить нельзя');
  });

  it('deletes a past-dated transfer: DELETE, reload, "Перевод удалён" toast', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(transfer1);
    component.confirmDeleteTransfer();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/transfers/${transfer1.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: [{ ...transfer1, isDeleted: true, deletedAt: '2026-09-11T00:00:00Z' }], pagination: { nextCursor: null, hasMore: false } });

    expect(component.successMessage()).toBe('Перевод удалён');
  });

  // ---- A2: the wallet filter sends exactly one walletId parameter ----

  it('applyFilters sends exactly one walletId parameter, with no separate source/target parameters', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.filterForm.controls.walletId.setValue('wallet-1');
    component.applyFilters();

    const req = httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`);
    expect(req.request.params.get('walletId')).toBe('wallet-1');
    expect(req.request.params.has('sourceWalletId')).toBe(false);
    expect(req.request.params.has('targetWalletId')).toBe(false);
    expect(req.request.params.keys().filter((key) => key.toLowerCase().includes('wallet'))).toEqual(['walletId']);

    req.flush({ data: [transfer1], pagination: { nextCursor: null, hasMore: false } });
  });

  // ---- A3: an empty filtered result is a distinct state from "no transfers at all" ----

  it('shows a dedicated "nothing found" state with a reset action when the applied filter returns no transfers', () => {
    const fixture = createComponent();
    const component = fixture.componentInstance;

    component.filterForm.controls.walletId.setValue('wallet-1');
    component.applyFilters();

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/transfers`)
      .flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    expect(component.hasActiveFilters()).toBe(true);
    expect(component.transfers().length).toBe(0);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('По этим условиям ничего не найдено');
    expect(compiled.textContent).not.toContain('Переводов пока нет');

    const resetButton = Array.from(compiled.querySelectorAll('button')).find((btn) => btn.textContent?.includes('Сбросить фильтры'));
    expect(resetButton).toBeTruthy();
  });

  it('shows the plain "no transfers yet" state (without a reset button) when there is no active filter', () => {
    const fixture = createComponent([]);

    const component = fixture.componentInstance;
    expect(component.hasActiveFilters()).toBe(false);

    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.textContent).toContain('Переводов пока нет');
    expect(compiled.textContent).not.toContain('По этим условиям ничего не найдено');
  });
});
