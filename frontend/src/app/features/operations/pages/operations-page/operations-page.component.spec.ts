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

const currencies = [{ id: 'c-usd', code: 'USD', name: 'US Dollar', isActive: true }];
const operationTypes = [{ id: 'ot-exp', name: 'Расход', behaviorKindId: 'bk-exp', isActive: true }];
const behaviorKinds = [{ id: 'bk-exp', code: 'Expense', name: 'Расход' }];

const operation1: Operation = {
  id: 'op-1',
  walletId: 'wallet-1',
  operationTypeId: 'ot-exp',
  amount: { amount: '12.50', currencyId: 'c-usd' },
  operationDate: '2026-09-10',
  adjustmentMode: null,
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
  deletedAt: '2026-09-11T08:30:00Z',
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

    expect(component.deletedBadgeTitle(deletedOperation)).toBe('Удалена 2026-09-11');
    expect(component.deletedBadgeTitle({ ...deletedOperation, deletedAt: null })).toBe('Удалена');

    const badge = (fixture.nativeElement as HTMLElement).querySelector('tbody tr td.actions app-badge') as HTMLElement;
    expect(badge.getAttribute('title')).toBe('Удалена 2026-09-11');
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

    expect(component.restoreError()).toBe('Операция не удалена.');
    expect(component.successMessage()).toBeNull();
    expect((fixture.nativeElement as HTMLElement).textContent).toContain('Операция не удалена.');
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
