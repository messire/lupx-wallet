import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PlannedTransactionWriteRequest } from '../../../../data-access/planning/planning-api.models';
import { PlanFormComponent, PlanTypeOption } from './plan-form.component';

describe('PlanFormComponent', () => {
  let fixture: ComponentFixture<PlanFormComponent>;
  let component: PlanFormComponent;
  let emitted: PlannedTransactionWriteRequest | undefined;

  const types: PlanTypeOption[] = [
    { type: { id: 'ot-rent', name: 'Аренда', behaviorKindId: 'bk-exp', isActive: true }, direction: 'Expense' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PlanFormComponent] }).compileComponents();
    fixture = TestBed.createComponent(PlanFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('types', types);
    fixture.componentRef.setInput('currencies', [{ id: 'c-rub', code: 'RUB', name: 'Рубль' }]);
    fixture.componentRef.setInput('wallets', [
      { id: 'w-rub', name: 'Карта', currencyId: 'c-rub', accountingStartDate: '2026-01-01' },
      { id: 'w-usd', name: 'USD', currencyId: 'c-usd', accountingStartDate: '2026-01-01' },
    ]);
    emitted = undefined;
    component.save.subscribe((value) => (emitted = value));
    fixture.detectChanges();
  });

  it('preselects the only currency and offers only wallets of that currency', () => {
    expect(component.form.controls.currencyId.value).toBe('c-rub');
    expect(component.walletOptions().map((wallet) => wallet.id)).toEqual(['w-rub']);
  });

  it('emits a one-off plan without recurrence', () => {
    component.form.patchValue({ operationTypeId: 'ot-rent', amount: ' 40000.00 ', startDate: '2026-11-10' });
    component.submit();

    expect(emitted).toEqual({
      operationTypeId: 'ot-rent',
      amount: '40000.00',
      currencyId: 'c-rub',
      walletId: null,
      description: null,
      startDate: '2026-11-10',
      recurrence: null,
    });
  });

  it('emits a recurrence with a count end and ignores the hidden until date', () => {
    component.form.patchValue({ operationTypeId: 'ot-rent', amount: '100.00', startDate: '2026-11-10' });
    component.form.controls.repeats.setValue(true);
    fixture.detectChanges();
    component.form.patchValue({ frequency: 'Weekly', interval: 2, endMode: 'count', count: 6 });
    fixture.detectChanges();
    component.submit();

    expect(emitted?.recurrence).toEqual({ frequency: 'Weekly', interval: 2, until: null, count: 6 });
  });

  it('rejects an end date before the first occurrence', () => {
    component.form.patchValue({ operationTypeId: 'ot-rent', amount: '100.00', startDate: '2026-11-10' });
    component.form.controls.repeats.setValue(true);
    fixture.detectChanges();
    component.form.controls.endMode.setValue('until');
    fixture.detectChanges();
    component.form.controls.until.setValue('2026-11-01');
    component.submit();

    expect(emitted).toBeUndefined();
    expect(component.form.controls.until.hasError('beforeStart')).toBe(true);
  });
});
