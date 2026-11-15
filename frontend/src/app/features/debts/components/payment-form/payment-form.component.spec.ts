import { ComponentFixture, TestBed } from '@angular/core/testing';
import { PaymentCreateRequest } from '../../../../data-access/debts/debts-api.models';
import { PaymentFormComponent } from './payment-form.component';

describe('PaymentFormComponent', () => {
  let fixture: ComponentFixture<PaymentFormComponent>;
  let component: PaymentFormComponent;
  let emitted: PaymentCreateRequest | undefined;

  function create(defaultAmount: string | null = null): void {
    fixture = TestBed.createComponent(PaymentFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('wallets', [{ id: 'w-1', name: 'Карта', currencyId: 'c-rub', accountingStartDate: '2026-03-01' }]);
    fixture.componentRef.setInput('operationTypes', [{ id: 'ot-1', name: 'Долги', behaviorKindId: 'bk', isActive: true }]);
    fixture.componentRef.setInput('remainingAmount', '90000.00');
    fixture.componentRef.setInput('currencyCode', 'RUB');
    fixture.componentRef.setInput('defaultAmount', defaultAmount);
    emitted = undefined;
    component.save.subscribe((value) => (emitted = value));
    fixture.detectChanges();
  }

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [PaymentFormComponent] }).compileComponents();
  });

  it('preselects the only wallet and type and prefills the default amount', () => {
    create('15000.00');
    expect(component.form.controls.walletId.value).toBe('w-1');
    expect(component.form.controls.operationTypeId.value).toBe('ot-1');
    expect(component.form.controls.amount.value).toBe('15000.00');
  });

  it('rejects an amount above the remaining one', () => {
    create();
    component.form.controls.amount.setValue('90000.01');
    component.submit();

    expect(emitted).toBeUndefined();
    expect(component.amountError()).toContain('не может превышать остаток');
  });

  it('rejects a date before the wallet accounting start', () => {
    create();
    component.form.patchValue({ amount: '100.00', paymentDate: '2026-02-28' });
    fixture.detectChanges();
    component.submit();

    expect(emitted).toBeUndefined();
    expect(component.form.controls.paymentDate.hasError('tooEarly')).toBe(true);
  });

  it('emits the payment with a trimmed amount equal to the remaining one', () => {
    create();
    component.form.patchValue({ amount: ' 90000.00 ', paymentDate: '2026-11-01' });
    component.submit();

    expect(emitted).toEqual({ walletId: 'w-1', operationTypeId: 'ot-1', amount: '90000.00', paymentDate: '2026-11-01' });
  });
});
