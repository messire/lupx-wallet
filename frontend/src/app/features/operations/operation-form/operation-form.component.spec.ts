import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OperationFormComponent } from './operation-form.component';
import { Operation, OperationTypeOption } from '../operations.models';

describe('OperationFormComponent', () => {
  let fixture: ComponentFixture<OperationFormComponent>;
  let component: OperationFormComponent;

  const wallets = [
    { id: 'wallet-1', name: 'Наличные' },
    { id: 'wallet-2', name: 'Карта' },
  ];

  const operationTypes: OperationTypeOption[] = [
    { id: 'type-income', name: 'Зарплата', behaviorKindCode: 'Income' },
    { id: 'type-adjustment', name: 'Корректировка', behaviorKindCode: 'Adjustment' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [OperationFormComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(OperationFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('wallets', wallets);
    fixture.componentRef.setInput('operationTypes', operationTypes);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('does not show adjustment mode for a non-adjustment type', () => {
    component.form.controls.operationTypeId.setValue('type-income');
    fixture.detectChanges();

    expect(component.isAdjustment()).toBe(false);
    expect(fixture.nativeElement.querySelector('.radio-group')).toBeNull();
  });

  it('shows adjustment mode selector only for Adjustment behaviorKind', () => {
    component.form.controls.operationTypeId.setValue('type-adjustment');
    fixture.detectChanges();

    expect(component.isAdjustment()).toBe(true);
    expect(fixture.nativeElement.querySelector('.radio-group')).not.toBeNull();
  });

  it('emits CreateOperationRequest without adjustmentMode for a non-adjustment type', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      walletId: 'wallet-1',
      operationTypeId: 'type-income',
      amount: '150.00',
      operationDate: '2026-09-01',
      adjustmentMode: 'Delta',
    });

    component.submit();

    expect(emitted).toEqual([
      {
        walletId: 'wallet-1',
        operationTypeId: 'type-income',
        amount: '150.00',
        operationDate: '2026-09-01',
      },
    ]);
  });

  it('emits adjustmentMode when the selected type behaviorKind is Adjustment', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      walletId: 'wallet-1',
      operationTypeId: 'type-adjustment',
      amount: '1000.00',
      operationDate: '2026-09-01',
      adjustmentMode: 'Absolute',
    });

    component.submit();

    expect(emitted).toEqual([
      {
        walletId: 'wallet-1',
        operationTypeId: 'type-adjustment',
        amount: '1000.00',
        operationDate: '2026-09-01',
        adjustmentMode: 'Absolute',
      },
    ]);
  });

  it('does not submit an invalid form', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.controls.amount.setValue('not-a-number');
    component.submit();

    expect(emitted).toEqual([]);
  });

  it('keeps the wallet field enabled and includes walletId when editing an existing operation', () => {
    const operation: Operation = {
      id: 'op-1',
      walletId: 'wallet-1',
      operationTypeId: 'type-income',
      amount: { amount: '150.00', currencyId: 'currency-1' },
      operationDate: '2026-09-01',
      adjustmentMode: null,
      transferId: null,
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    };

    fixture.componentRef.setInput('editingOperation', operation);
    fixture.detectChanges();

    expect(component.form.controls.walletId.disabled).toBe(false);

    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));
    component.form.controls.amount.setValue('175.00');
    component.submit();

    expect(emitted).toEqual([
      {
        walletId: 'wallet-1',
        operationTypeId: 'type-income',
        amount: '175.00',
        operationDate: '2026-09-01',
      },
    ]);
  });

  it('emits the new walletId when the wallet is changed while editing (UC-13, перенос операции)', () => {
    const operation: Operation = {
      id: 'op-1',
      walletId: 'wallet-1',
      operationTypeId: 'type-income',
      amount: { amount: '150.00', currencyId: 'currency-1' },
      operationDate: '2026-09-01',
      adjustmentMode: null,
      transferId: null,
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    };

    fixture.componentRef.setInput('editingOperation', operation);
    fixture.detectChanges();

    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));
    component.form.controls.walletId.setValue('wallet-2');
    component.submit();

    expect(emitted).toEqual([
      {
        walletId: 'wallet-2',
        operationTypeId: 'type-income',
        amount: '150.00',
        operationDate: '2026-09-01',
      },
    ]);
  });

  it('emits cancel', () => {
    const emitted: unknown[] = [];
    component.cancel.subscribe(() => emitted.push(true));

    component.onCancel();

    expect(emitted.length).toBe(1);
  });
});
