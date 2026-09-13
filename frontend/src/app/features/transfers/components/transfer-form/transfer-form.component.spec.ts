import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TransferFormComponent, TransferWalletOption } from './transfer-form.component';

describe('TransferFormComponent', () => {
  let fixture: ComponentFixture<TransferFormComponent>;
  let component: TransferFormComponent;

  const wallets: TransferWalletOption[] = [
    { id: 'wallet-1', name: 'Наличные USD', currencyId: 'currency-usd' },
    { id: 'wallet-2', name: 'Карта USD', currencyId: 'currency-usd' },
    { id: 'wallet-3', name: 'Карта EUR', currencyId: 'currency-eur' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TransferFormComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(TransferFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('wallets', wallets);
    fixture.detectChanges();
  });

  it('should create', () => {
    expect(component).toBeTruthy();
  });

  it('offers only same-currency wallets (excluding the source) as transfer targets (UC-16/UC-17)', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1');
    fixture.detectChanges();

    expect(component.targetWalletOptions()).toEqual([{ id: 'wallet-2', name: 'Карта USD', currencyId: 'currency-usd' }]);
  });

  it('resets an incompatible target when the source wallet changes', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1');
    component.form.controls.targetWalletId.setValue('wallet-2');

    component.form.controls.sourceWalletId.setValue('wallet-3');

    expect(component.form.controls.targetWalletId.value).toBe('');
  });

  it('marks the form invalid when source and target wallets are the same', () => {
    component.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-1',
      amount: '100.00',
      transferDate: '2026-09-01',
    });

    expect(component.form.errors?.['sameWallet']).toBe(true);
    expect(component.form.invalid).toBe(true);
  });

  it('emits CreateTransferRequest on submit with a valid form', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-2',
      amount: '250.50',
      transferDate: '2026-09-01',
    });

    component.submit();

    expect(emitted).toEqual([
      {
        sourceWalletId: 'wallet-1',
        targetWalletId: 'wallet-2',
        amount: '250.50',
        transferDate: '2026-09-01',
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

  it('emits cancel', () => {
    const emitted: unknown[] = [];
    component.cancel.subscribe(() => emitted.push(true));

    component.onCancel();

    expect(emitted.length).toBe(1);
  });
});
