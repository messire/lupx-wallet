import { ComponentFixture, TestBed } from '@angular/core/testing';
import { OperationFormComponent } from './operation-form.component';
import { Operation } from '../../../../data-access/operations/operations-api.models';
import { OperationTypeOption } from '../../operations.models';

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

  it('labels the amount "Сумма" for a non-adjustment type', () => {
    component.form.controls.operationTypeId.setValue('type-income');
    fixture.detectChanges();

    expect(component.isAdjustment()).toBe(false);
    expect(fixture.nativeElement.querySelector('app-radio-group')).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('Изменение на сумму');
  });

  it('offers only the signed change for an Adjustment type: no mode selector, no "Новое значение баланса"', () => {
    component.form.controls.operationTypeId.setValue('type-adjustment');
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(component.isAdjustment()).toBe(true);
    expect(fixture.nativeElement.querySelector('app-radio-group')).toBeNull();
    expect(text).toContain('Изменение на сумму (+/−)');
    expect(text).toContain('плюс увеличивает баланс, минус уменьшает');
    // The hint points to the other way of stating a balance and speaks the same words as the label.
    expect(text).toContain('Изменение на сумму: плюс увеличивает');
    expect(text).toContain('Чтобы задать фактический баланс на дату, используйте «Указать баланс»');
    expect(text).not.toContain('Изменение баланса');
    expect(text).not.toContain('Новое значение баланса');
    expect(text).not.toContain('Новый баланс');
  });

  it('emits CreateOperationRequest without adjustmentMode for a non-adjustment type', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      walletId: 'wallet-1',
      operationTypeId: 'type-income',
      amount: '150.00',
      operationDate: '2026-09-01',
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

  it('emits adjustmentMode Delta (and a negative amount as is) when the selected type behaviorKind is Adjustment', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      walletId: 'wallet-1',
      operationTypeId: 'type-adjustment',
      amount: '-1000.00',
      operationDate: '2026-09-01',
    });

    component.submit();

    expect(emitted).toEqual([
      {
        walletId: 'wallet-1',
        operationTypeId: 'type-adjustment',
        amount: '-1000.00',
        operationDate: '2026-09-01',
        adjustmentMode: 'Delta',
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
      isCorrection: false,
      statedBalance: null,
      transferId: null,
      isDeleted: false,
      deletedAt: null,
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
      isCorrection: false,
      statedBalance: null,
      transferId: null,
      isDeleted: false,
      deletedAt: null,
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

  it('keeps adjustmentMode Delta and the signed amount when an existing adjustment is edited', () => {
    const adjustment: Operation = {
      id: 'op-adj',
      walletId: 'wallet-1',
      operationTypeId: 'type-adjustment',
      amount: { amount: '-40.00', currencyId: 'currency-1' },
      operationDate: '2026-09-01',
      adjustmentMode: 'Delta',
      isCorrection: false,
      statedBalance: null,
      transferId: null,
      isDeleted: false,
      deletedAt: null,
      createdAt: '2026-09-01T00:00:00Z',
      updatedAt: '2026-09-01T00:00:00Z',
    };
    fixture.componentRef.setInput('editingOperation', adjustment);
    fixture.detectChanges();

    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(component.isAdjustment()).toBe(true);
    expect(text).toContain('Изменение на сумму (+/−)');
    expect(fixture.nativeElement.querySelector('app-radio-group')).toBeNull();

    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));
    component.form.controls.amount.setValue('-45.50');
    component.submit();

    expect(emitted).toEqual([
      {
        walletId: 'wallet-1',
        operationTypeId: 'type-adjustment',
        amount: '-45.50',
        operationDate: '2026-09-01',
        adjustmentMode: 'Delta',
      },
    ]);
  });

  it('drops adjustmentMode and the adjustment wording when the type is switched from an adjustment to an income', () => {
    component.form.controls.operationTypeId.setValue('type-adjustment');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Изменение на сумму (+/−)');

    component.form.controls.operationTypeId.setValue('type-income');
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).not.toContain('Изменение на сумму (+/−)');

    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));
    component.form.patchValue({ walletId: 'wallet-1', amount: '10', operationDate: '2026-09-01' });
    component.submit();

    expect(emitted).toEqual([
      { walletId: 'wallet-1', operationTypeId: 'type-income', amount: '10', operationDate: '2026-09-01' },
    ]);
  });

  it('offers no "Absolute"-style choice anywhere: no radio, no "Новое значение", no "Новый баланс"', () => {
    for (const typeId of ['type-income', 'type-adjustment']) {
      component.form.controls.operationTypeId.setValue(typeId);
      fixture.detectChanges();
      const root = fixture.nativeElement as HTMLElement;
      expect(root.querySelector('input[type="radio"]')).toBeNull();
      expect(root.textContent).not.toContain('Новое значение');
      expect(root.textContent).not.toContain('Новый баланс');
    }
  });

  it('emits cancel', () => {
    const emitted: unknown[] = [];
    component.cancel.subscribe(() => emitted.push(true));

    component.onCancel();

    expect(emitted.length).toBe(1);
  });
});
