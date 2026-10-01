import { HttpClientTestingModule, HttpTestingController } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { environment } from '../../../../../environments/environment';
import { Operation } from '../../../../data-access/operations/operations-api.models';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { OperationsComponent } from './operations-page.component';

const wallet: Wallet = {
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

const currencies = [{ id: 'c-usd', code: 'USD', name: 'US Dollar' }];
const operationTypes = [{ id: 'ot-exp', name: 'Расход', behaviorKindId: 'bk-exp', isActive: true }];
const behaviorKinds = [{ id: 'bk-exp', code: 'Expense', name: 'Расход' }];

const operation1: Operation = {
  id: 'op-1',
  walletId: 'wallet-1',
  operationTypeId: 'ot-exp',
  amount: { amount: '12.50', currencyId: 'c-usd' },
  operationDate: '2026-09-10',
  adjustmentMode: null,
  isCorrection: false,
  statedBalance: null,
  transferId: null,
  isDeleted: false,
  deletedAt: null,
  createdAt: '2026-09-10T00:00:00Z',
  updatedAt: '2026-09-10T00:00:00Z',
};

const deletedOperation: Operation = {
  ...operation1,
  id: 'op-deleted',
  isDeleted: true,
  deletedAt: '2026-09-11T12:00:00Z',
};

const emptyPage = { pagination: { nextCursor: null, hasMore: false } };

/**
 * jsdom does not implement `HTMLDialogElement.prototype.showModal`/`close` (see
 * `shared/ui/dialog/dialog.component.spec.ts`) — DialogComponent calls them.
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

describe('OperationsComponent', () => {
  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OperationsComponent, HttpClientTestingModule],
      providers: [provideRouter([])],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
  });

  function createComponent(operations: Operation[] = [operation1]): ComponentFixture<OperationsComponent> {
    const fixture = TestBed.createComponent(OperationsComponent);
    fixture.detectChanges();

    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/wallets`).flush({ data: [wallet], ...emptyPage });
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/currencies`).flush({ data: currencies, ...emptyPage });
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/operation-types`)
      .flush({ data: operationTypes, ...emptyPage });
    httpMock.expectOne(`${environment.apiBaseUrl}/operation-behavior-kinds`).flush(behaviorKinds);
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`).flush({ data: operations, ...emptyPage });

    fixture.detectChanges();
    return fixture;
  }

  function rowActionsText(fixture: ComponentFixture<OperationsComponent>): string {
    return (fixture.nativeElement as HTMLElement).querySelector('.row-actions-list')?.textContent ?? '';
  }

  // ---- ADR-0002 §3/§5: a deleted operation stays in the list, marked ----

  it('marks a deleted operation row with is-deleted and the "Удалена" badge, and leaves an active row unmarked', () => {
    const fixture = createComponent([operation1, deletedOperation]);
    const rows = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr');

    expect(rows.length).toBe(2);
    expect(rows[0].classList.contains('is-deleted')).toBe(false);
    expect(rows[0].textContent).not.toContain('Удалена');
    expect(rows[1].classList.contains('is-deleted')).toBe(true);
    expect(rows[1].textContent).toContain('Удалена');
  });

  it('puts the deletion date into the tooltip of the "Удалена" badge', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    expect(component.deletedBadgeTitle(deletedOperation)).toBe('Удалена 11.09.2026');
    expect(component.deletedBadgeTitle({ ...deletedOperation, deletedAt: null })).toBe('Удалена');

    const badge = (fixture.nativeElement as HTMLElement).querySelector('tbody tr td.date-col app-badge') as HTMLElement;
    expect(badge.getAttribute('title')).toBe('Удалена 11.09.2026');
  });

  it('puts the "Удалена" badge into the date cell (read before the row data) and not into the actions cell', () => {
    const fixture = createComponent([deletedOperation]);
    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr') as HTMLElement;

    expect(row.querySelector('td:first-child')?.textContent).toContain('Удалена');
    expect(row.querySelector('td.actions')?.textContent).not.toContain('Удалена');
  });

  it('names the "···" button with the date, type and signed amount of its row', () => {
    const fixture = createComponent([operation1]);
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('tbody tr td.actions button') as HTMLElement;

    expect(trigger.getAttribute('aria-label')).toContain('Действия с операцией: 2026-09-10, Расход, −');
    expect(trigger.getAttribute('aria-label')).toContain('12');
  });

  it('shows the visible "Открыть перевод" link to /transfers for a transfer leg, without the "···" menu', () => {
    const fixture = createComponent([{ ...operation1, transferId: 't-1' }]);
    const actions = (fixture.nativeElement as HTMLElement).querySelector('tbody tr td.actions') as HTMLElement;

    const link = Array.from(actions.querySelectorAll('a')).find((a) => a.textContent?.includes('Открыть перевод'));
    expect(link).toBeTruthy();
    expect(link?.getAttribute('href')).toBe('/transfers');
    expect(actions.querySelector('button')).toBeNull();
  });

  it('shows the same "Открыть перевод" link for a deleted transfer leg', () => {
    const fixture = createComponent([{ ...deletedOperation, transferId: 't-1' }]);
    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr') as HTMLElement;

    expect(row.classList.contains('is-deleted')).toBe(true);
    expect(row.querySelector('td.actions a[href="/transfers"]')?.textContent).toContain('Открыть перевод');
    expect(row.querySelector('td.actions button')).toBeNull();
  });

  it('keeps the "···" trigger available in the actions cell of a deleted row (the cell is not struck through)', () => {
    const fixture = createComponent([deletedOperation]);
    const trigger = (fixture.nativeElement as HTMLElement).querySelector('tbody tr td.actions button');

    expect(trigger).toBeTruthy();
  });

  // ---- "···" menu ----

  it('offers "Восстановить" and hides "Изменить"/"Удалить" in the menu of a deleted operation', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.openRowActions(deletedOperation);
    fixture.detectChanges();

    const text = rowActionsText(fixture);
    expect(text).toContain('Восстановить');
    expect(text).not.toContain('Изменить');
    expect(text).not.toContain('Удалить');
    expect(text).toContain('История изменений');
  });

  it('offers "Изменить"/"Удалить" and hides "Восстановить" in the menu of an active operation', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.openRowActions(operation1);
    fixture.detectChanges();

    const text = rowActionsText(fixture);
    expect(text).toContain('Изменить');
    expect(text).toContain('Удалить');
    expect(text).not.toContain('Восстановить');
  });

  // ---- Restore: no confirmation, POST /operations/{id}/restore ----

  it('restores a deleted operation without confirmation: POST restore, reload of the list, success toast', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.openRowActions(deletedOperation);
    component.restoreFromRowActions(deletedOperation);
    // Nothing is sent until the row-actions dialog has finished closing.
    httpMock.expectNone(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`);
    component.onRowActionsDialogClosed();

    expect(component.pendingDeleteOperation()).toBeNull();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`);
    expect(req.request.method).toBe('POST');
    req.flush({ ...deletedOperation, isDeleted: false, deletedAt: null });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`)
      .flush({ data: [{ ...deletedOperation, isDeleted: false, deletedAt: null }], ...emptyPage });
    fixture.detectChanges();

    expect(component.successMessage()).toBe('Операция восстановлена');
    expect(component.restoreError()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Операция восстановлена');
    expect((fixture.nativeElement as HTMLElement).querySelector('tbody tr')?.classList.contains('is-deleted')).toBe(false);
  });

  it('sets restoreError (readable server detail) and does not reload or show a toast when restoring fails', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.restoreOperation(deletedOperation);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`);
    req.flush(
      { title: 'Conflict', status: 409, detail: 'Операция не удалена.' },
      { status: 409, statusText: 'Conflict' },
    );
    fixture.detectChanges();

    // 409 means the list on screen is stale (changed in another tab) - it is reloaded.
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`)
      .flush({ data: [{ ...deletedOperation, isDeleted: false, deletedAt: null }], ...emptyPage });
    fixture.detectChanges();

    expect(component.restoreError()).toBe('Операция не удалена.');
    expect(component.successMessage()).toBeNull();
    const toast = (fixture.nativeElement as HTMLElement).querySelector('app-toast .app-toast') as HTMLElement;
    expect(toast.textContent).toContain('Операция не удалена.');
    expect(toast.getAttribute('role')).toBe('alert');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-error-state')).toBeNull();
    expect((fixture.nativeElement as HTMLElement).querySelector('tbody tr')?.classList.contains('is-deleted')).toBe(false);
  });

  it('does not reload the list when restoring fails with a non-conflict error', () => {
    const fixture = createComponent([deletedOperation]);

    fixture.componentInstance.restoreOperation(deletedOperation);
    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/operations`);
  });

  it('ignores a repeated restore while the first request is in flight', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.restoreOperation(deletedOperation);
    component.restoreOperation(deletedOperation);

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`);
    expect(component.isRestoring()).toBe(true);
    req.flush(deletedOperation);
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`).flush({ data: [], ...emptyPage });
    expect(component.isRestoring()).toBe(false);
  });

  it('falls back to a generic restoreError text when the server sends no problem details', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.restoreOperation(deletedOperation);

    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.restoreError()).toBe('Не удалось восстановить операцию.');
  });

  it('clears a previous restoreError when a new restore attempt starts', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.restoreOperation(deletedOperation);
    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`)
      .flush(null, { status: 500, statusText: 'Server Error' });
    expect(component.restoreError()).not.toBeNull();

    component.restoreOperation(deletedOperation);
    expect(component.restoreError()).toBeNull();
    httpMock.expectOne(`${environment.apiBaseUrl}/operations/${deletedOperation.id}/restore`).flush(deletedOperation);
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`).flush({ data: [], ...emptyPage });
  });

  it('does not send a restore request for an active operation or for a transfer leg', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.restoreOperation(operation1);
    component.restoreOperation({ ...deletedOperation, transferId: 't-1' });

    httpMock.expectNone((r) => r.url.endsWith('/restore'));
    expect(component.restoreError()).toBeNull();
  });

  // ---- Delete confirmation text (ADR-0002: soft, reversible, any date) ----

  it('shows the new delete-confirm text: record stays in the list, is excluded from calculations, can be restored', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.openRowActions(operation1);
    component.deleteFromRowActions(operation1);
    component.onRowActionsDialogClosed();
    fixture.detectChanges();

    expect(component.pendingDeleteOperation()).toEqual(operation1);

    const dialogText = (fixture.nativeElement as HTMLElement).textContent?.replace(/\s+/g, ' ') ?? '';
    expect(dialogText).toContain('Запись останется в списке, исключится из расчётов');
    expect(dialogText).toContain('Операцию можно восстановить');
    expect(dialogText).toContain('Наличные USD');
    expect(dialogText).toContain('2026-09-10');
    expect(dialogText).not.toContain('уже учтена в истории баланса');
    expect(dialogText).not.toContain('без возможности восстановления');
  });

  it('titles the delete-confirm dialog as a question', () => {
    const fixture = createComponent([operation1]);

    fixture.componentInstance.requestDeleteConfirm(operation1);
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Удалить операцию?');
  });

  it('deletes an operation with any (past) date: DELETE, reload, "Операция удалена" toast', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(operation1); // 2026-09-10 — in the past
    component.confirmDeleteOperation();

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/${operation1.id}`);
    expect(req.request.method).toBe('DELETE');
    req.flush(null, { status: 204, statusText: 'No Content' });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`)
      .flush({ data: [deletedOperation], ...emptyPage });

    expect(component.successMessage()).toBe('Операция удалена');
    expect(component.deleteError()).toBeNull();
  });

  it('uses the generic fallback text (without the retired "history" wording) when deletion fails without details', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(operation1);
    component.confirmDeleteOperation();

    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/${operation1.id}`)
      .flush(null, { status: 500, statusText: 'Server Error' });

    expect(component.deleteError()).toBe('Не удалось удалить операцию.');
  });

  it('keeps the delete-confirm dialog open and shows the delete error inside it (no error-state above the list)', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(operation1);
    component.confirmDeleteOperation();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/${operation1.id}`)
      .flush({ title: 'Error', status: 500, detail: 'Сбой сервера.' }, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(component.pendingDeleteOperation()).toEqual(operation1);
    expect(component.isDeleting()).toBe(false);
    const alert = (fixture.nativeElement as HTMLElement).querySelector('app-dialog p.form-error[role="alert"]');
    expect(alert?.textContent).toContain('Сбой сервера.');
    expect((fixture.nativeElement as HTMLElement).querySelector('app-error-state')).toBeNull();
  });

  it('reloads the list after a 409 on delete (already deleted in another tab) and keeps the error in the dialog', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(operation1);
    component.confirmDeleteOperation();
    httpMock
      .expectOne(`${environment.apiBaseUrl}/operations/${operation1.id}`)
      .flush({ title: 'Conflict', status: 409, detail: 'Операция уже удалена.' }, { status: 409, statusText: 'Conflict' });

    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`)
      .flush({ data: [{ ...operation1, isDeleted: true, deletedAt: '2026-09-11T12:00:00Z' }], ...emptyPage });
    fixture.detectChanges();

    expect(component.deleteError()).toBe('Операция уже удалена.');
    expect(component.pendingDeleteOperation()).toEqual(operation1);
    expect((fixture.nativeElement as HTMLElement).querySelector('tbody tr')?.classList.contains('is-deleted')).toBe(true);
  });

  it('ignores a repeated delete confirmation while the first request is in flight and blocks closing meanwhile', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(operation1);
    component.confirmDeleteOperation();
    component.confirmDeleteOperation();
    const req = httpMock.expectOne(`${environment.apiBaseUrl}/operations/${operation1.id}`);

    const event = new Event('cancel', { cancelable: true });
    component.onDeleteDismissRequested(event);
    component.cancelDeleteConfirm();
    expect(event.defaultPrevented).toBe(true);
    expect(component.pendingDeleteOperation()).toEqual(operation1);

    req.flush(null, { status: 204, statusText: 'No Content' });
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`).flush({ data: [], ...emptyPage });
    expect(component.pendingDeleteOperation()).toBeNull();
  });

  // ---- Guards: a deleted operation can be neither edited nor deleted ----

  it('openEditForm rejects a deleted operation (it must be restored first)', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.openEditForm(deletedOperation);

    expect(component.isFormOpen()).toBe(false);
    expect(component.editingOperation()).toBeNull();
  });

  it('openEditForm still opens an active operation and rejects a transfer leg', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.openEditForm({ ...operation1, transferId: 't-1' });
    expect(component.isFormOpen()).toBe(false);

    component.openEditForm(operation1);
    expect(component.isFormOpen()).toBe(true);
    expect(component.editingOperation()).toEqual(operation1);
  });

  it('requestDeleteConfirm rejects a deleted operation', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.requestDeleteConfirm(deletedOperation);

    expect(component.pendingDeleteOperation()).toBeNull();
  });

  it('the row-actions follow-up for a deleted operation never opens the edit form or delete confirm', () => {
    const fixture = createComponent([deletedOperation]);
    const component = fixture.componentInstance;

    component.editFromRowActions(deletedOperation);
    component.onRowActionsDialogClosed();
    component.deleteFromRowActions(deletedOperation);
    component.onRowActionsDialogClosed();

    expect(component.isFormOpen()).toBe(false);
    expect(component.pendingDeleteOperation()).toBeNull();
  });

  // ---- ADR-0003 §6: correction amount (system row) ----

  const negativeCorrection: Operation = {
    ...operation1,
    id: 'op-corr-neg',
    operationTypeId: null,
    amount: { amount: '-50.00', currencyId: 'c-usd' },
    operationDate: '2026-09-12',
    isCorrection: true,
    statedBalance: { amount: '350.00', currencyId: 'c-usd' },
  };

  const positiveCorrection: Operation = {
    ...negativeCorrection,
    id: 'op-corr-pos',
    amount: { amount: '50.00', currencyId: 'c-usd' },
  };

  it('renders a negative correction as "Неизвестные траты" with the "Система" badge and the stated balance', () => {
    const fixture = createComponent([negativeCorrection]);
    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr') as HTMLElement;
    const cells = row.querySelectorAll('td');

    expect(row.classList.contains('is-system')).toBe(true);
    expect(row.classList.contains('is-deleted')).toBe(false);
    // The badge sits in the date cell, so a screen reader reads it before the row data.
    expect(cells[0].textContent).toContain('Система');
    expect(cells[2].textContent).toContain('Неизвестные траты');
    expect(cells[2].textContent).not.toContain('Неизвестные доходы');
    expect(cells[3].textContent?.replace(/\s+/g, ' ').trim()).toBe('−50.00 USD');
    expect(cells[4].textContent).toContain('Баланс на конец дня: 350.00 USD');
  });

  it('renders a positive correction as "Неизвестные доходы" with an explicit plus sign', () => {
    const fixture = createComponent([positiveCorrection]);
    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr td');

    expect(cells[2].textContent).toContain('Неизвестные доходы');
    expect(cells[3].textContent?.replace(/\s+/g, ' ').trim()).toBe('+50.00 USD');
  });

  it('shows only the change-history link in a correction row, named with the title and the date', () => {
    const fixture = createComponent([negativeCorrection]);
    const actions = (fixture.nativeElement as HTMLElement).querySelector('tbody tr td.actions') as HTMLElement;

    expect(actions.querySelector('button')).toBeNull();
    const links = actions.querySelectorAll('a');
    expect(links.length).toBe(1);
    expect(links[0].textContent).toContain('История изменений');
    expect(links[0].getAttribute('aria-label')).toBe('История изменений: Неизвестные траты, 2026-09-12');
    expect(links[0].getAttribute('href')).toContain('entityId=op-corr-neg');
  });

  it('shows "Изменение на сумму" instead of the raw Delta in the adjustment column of an adjustment', () => {
    const fixture = createComponent([{ ...operation1, adjustmentMode: 'Delta' }]);
    const cells = (fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr td');

    expect(cells[4].textContent?.trim()).toBe('Изменение на сумму');
  });

  it('never offers edit, delete or restore for a correction, even if its menu is opened', () => {
    const fixture = createComponent([negativeCorrection]);
    const component = fixture.componentInstance;

    component.openRowActions(negativeCorrection);
    fixture.detectChanges();
    const text = rowActionsText(fixture);
    expect(text).toContain('История изменений');
    expect(text).not.toContain('Изменить');
    expect(text).not.toContain('Удалить');
    expect(text).not.toContain('Восстановить');

    component.openEditForm(negativeCorrection);
    component.requestDeleteConfirm(negativeCorrection);
    component.restoreOperation({ ...negativeCorrection, isDeleted: true });
    expect(component.isFormOpen()).toBe(false);
    expect(component.pendingDeleteOperation()).toBeNull();
    httpMock.expectNone((r) => r.url.endsWith('/restore'));
  });

  it('shows "—" for an absent operation type', () => {
    const fixture = createComponent([operation1]);

    expect(fixture.componentInstance.operationTypeName(null)).toBe('—');
  });

  // ---- "Указать баланс" ----

  function openStatedBalanceDialog(fixture: ComponentFixture<OperationsComponent>): HTMLElement {
    const root = fixture.nativeElement as HTMLElement;
    const trigger = Array.from(root.querySelectorAll('.page-heading button')).find((button) =>
      button.textContent?.includes('Указать баланс'),
    ) as HTMLButtonElement;
    trigger.click();
    fixture.detectChanges();
    return root;
  }

  it('has an "Указать баланс" action next to "Добавить операцию" that opens the form with the hint', () => {
    const fixture = createComponent([operation1]);
    const root = openStatedBalanceDialog(fixture);

    expect(fixture.componentInstance.isStatedBalanceFormOpen()).toBe(true);
    expect(root.querySelector('app-stated-balance-form')).not.toBeNull();
    expect(root.querySelector('.page-heading')?.textContent).toContain('Добавить операцию');
    expect(root.querySelector('app-stated-balance-form')?.textContent).toContain('Неизвестные траты');
  });

  it('submits the stated balance with PUT, reloads the list and reports 201 as "указан"', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;
    openStatedBalanceDialog(fixture);

    component.saveStatedBalance({ walletId: 'wallet-1', date: '2026-09-10', amount: '350.00' });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2026-09-10`);
    expect(req.request.method).toBe('PUT');
    expect(req.request.body).toEqual({ amount: '350.00' });
    req.flush(positiveCorrection, { status: 201, statusText: 'Created' });
    httpMock
      .expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`)
      .flush({ data: [operation1, positiveCorrection], ...emptyPage });
    fixture.detectChanges();

    expect(component.isStatedBalanceFormOpen()).toBe(false);
    expect(component.isStatedBalanceSubmitting()).toBe(false);
    expect(component.successMessage()).toBe('Баланс на 2026-09-10 указан');
    expect((fixture.nativeElement as HTMLElement).querySelectorAll('tbody tr').length).toBe(2);
  });

  it('reports 200 as a replacement of the previous value', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.saveStatedBalance({ walletId: 'wallet-1', date: '2026-09-10', amount: '350.00' });
    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2026-09-10`)
      .flush(positiveCorrection, { status: 200, statusText: 'OK' });
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`).flush({ data: [], ...emptyPage });

    expect(component.successMessage()).toBe('Баланс на 2026-09-10 заменён: прежнее значение обновлено');
  });

  it('keeps the form open and shows the server detail of a 400 (date out of range) as an alert', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;
    openStatedBalanceDialog(fixture);

    component.saveStatedBalance({ walletId: 'wallet-1', date: '2025-01-01', amount: '1' });
    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2025-01-01`)
      .flush(
        { title: 'Bad Request', status: 400, detail: 'Дата раньше начала учета кошелька.' },
        { status: 400, statusText: 'Bad Request' },
      );
    fixture.detectChanges();

    expect(component.isStatedBalanceFormOpen()).toBe(true);
    expect(component.isStatedBalanceSubmitting()).toBe(false);
    const alert = (fixture.nativeElement as HTMLElement).querySelector('app-stated-balance-form p.form-error[role="alert"]');
    expect(alert?.textContent).toContain('Дата раньше начала учета кошелька.');
    expect(component.successMessage()).toBeNull();
    httpMock.expectNone((r) => r.url === `${environment.apiBaseUrl}/operations`);
  });

  it('uses readable fallback texts for 400 and 409 without problem details', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.saveStatedBalance({ walletId: 'wallet-1', date: '2025-01-01', amount: '1' });
    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2025-01-01`)
      .flush(null, { status: 400, statusText: 'Bad Request' });
    expect(component.statedBalanceError()).toContain('от начала учёта кошелька до сегодняшнего дня');

    component.saveStatedBalance({ walletId: 'wallet-1', date: '2025-01-01', amount: '1' });
    httpMock
      .expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2025-01-01`)
      .flush(null, { status: 409, statusText: 'Conflict' });
    expect(component.statedBalanceError()).toBe('Запись изменена параллельно. Повторите ввод.');
  });

  it('ignores a repeated submit while the first PUT is in flight', () => {
    const fixture = createComponent([operation1]);
    const component = fixture.componentInstance;

    component.saveStatedBalance({ walletId: 'wallet-1', date: '2026-09-10', amount: '1' });
    component.saveStatedBalance({ walletId: 'wallet-1', date: '2026-09-10', amount: '1' });

    const req = httpMock.expectOne(`${environment.apiBaseUrl}/wallets/wallet-1/stated-balances/2026-09-10`);
    req.flush(positiveCorrection, { status: 201, statusText: 'Created' });
    httpMock.expectOne((r) => r.url === `${environment.apiBaseUrl}/operations`).flush({ data: [], ...emptyPage });
  });

  it('treats a response without isDeleted (older backend) as an active row', () => {
    const legacy = { ...operation1 } as Partial<Operation>;
    delete legacy.isDeleted;
    delete legacy.deletedAt;
    const fixture = createComponent([legacy as Operation]);

    const row = (fixture.nativeElement as HTMLElement).querySelector('tbody tr') as HTMLElement;
    expect(row.classList.contains('is-deleted')).toBe(false);
    expect(row.textContent).not.toContain('Удалена');
  });
});
