import { ComponentFixture, TestBed } from '@angular/core/testing';
import { TransferFormComponent, TransferWalletOption } from './transfer-form.component';

describe('TransferFormComponent', () => {
  let fixture: ComponentFixture<TransferFormComponent>;
  let component: TransferFormComponent;

  const wallets: TransferWalletOption[] = [
    { id: 'wallet-1', name: 'Наличные USD', currencyId: 'currency-usd', currencyCode: 'USD', accountingStartDate: '2026-01-01' },
    { id: 'wallet-2', name: 'Карта USD', currencyId: 'currency-usd', currencyCode: 'USD', accountingStartDate: '2026-02-01' },
    { id: 'wallet-3', name: 'Карта EUR', currencyId: 'currency-eur', currencyCode: 'EUR', accountingStartDate: '2026-01-01' },
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

    expect(component.targetWalletOptions()).toEqual([
      { id: 'wallet-2', name: 'Карта USD', currencyId: 'currency-usd', currencyCode: 'USD', accountingStartDate: '2026-02-01' },
    ]);
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

  // ---- A7: changing the source resets the currently selected target ----

  it('resets a compatible target as well, whenever the source wallet changes', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1');
    component.form.controls.targetWalletId.setValue('wallet-2');

    // wallet-2 is USD, same as wallet-1 — still cleared: the target belongs to the
    // *previous* source and must be re-chosen against the new one.
    component.form.controls.sourceWalletId.setValue('wallet-1');

    expect(component.form.controls.targetWalletId.value).toBe('');
  });

  it('leaves an empty target alone when the source wallet changes', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1');

    expect(component.form.controls.targetWalletId.value).toBe('');
  });

  // ---- A7: no compatible target wallet -> a plain-language explanation, target select disabled ----

  it('explains in plain language when there is no compatible target wallet, and disables the target select', () => {
    component.form.controls.sourceWalletId.setValue('wallet-3'); // the only EUR wallet
    fixture.detectChanges();

    expect(component.targetWalletOptions()).toEqual([]);
    const hint = component.targetWalletHint();
    expect(hint).toBeTruthy();
    expect(hint).not.toMatch(/UC-\d+/);

    const select = fixture.nativeElement.querySelector('select[formcontrolname="targetWalletId"]') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
  });

  it('shows "Сначала выберите источник" before any source is selected, and disables the target select', () => {
    const hint = component.targetWalletHint();

    expect(hint).toBe('Сначала выберите источник');
    const select = fixture.nativeElement.querySelector('select[formcontrolname="targetWalletId"]') as HTMLSelectElement;
    expect(select.disabled).toBe(true);
  });

  // ---- A8: date min/max computed from both wallets' accountingStartDate and today ----

  it('computes the date lower bound as the later accountingStartDate of the two selected wallets', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1'); // 2026-01-01
    component.form.controls.targetWalletId.setValue('wallet-2'); // 2026-02-01
    fixture.detectChanges();

    expect(component.minTransferDate()).toBe('2026-02-01');

    const dateInput = fixture.nativeElement.querySelector('input[type="date"]') as HTMLInputElement;
    expect(dateInput.min).toBe('2026-02-01');
  });

  it('has no lower bound before a source wallet is selected, and an upper bound of today', () => {
    expect(component.minTransferDate()).toBeNull();

    const today = new Date().toISOString().slice(0, 10);
    expect(component.maxTransferDate).toBe(today);

    const dateInput = fixture.nativeElement.querySelector('input[type="date"]') as HTMLInputElement;
    expect(dateInput.max).toBe(today);
  });

  it('rejects a date earlier than the computed lower bound', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1');
    component.form.controls.targetWalletId.setValue('wallet-2'); // min = 2026-02-01
    fixture.detectChanges();

    component.form.controls.transferDate.setValue('2026-01-15');

    expect(component.form.controls.transferDate.errors?.['transferDateTooEarly']).toBe(true);
  });

  // ---- A6: the amount field shows the source wallet's currency code as an input-suffix ----

  it('shows the source wallet\'s currency code next to the amount field', () => {
    component.form.controls.sourceWalletId.setValue('wallet-1'); // USD
    fixture.detectChanges();

    const suffix = fixture.nativeElement.querySelector('.input-suffix__code') as HTMLElement;
    expect(suffix.textContent?.trim()).toBe('USD');
  });

  it('shows no currency code before a source wallet is selected', () => {
    const suffix = fixture.nativeElement.querySelector('.input-suffix__code');
    expect(suffix).toBeNull();
  });

  // ---- Amount validation: negative and non-numeric amounts are rejected ----

  it('does not submit with a negative amount', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-2',
      amount: '-100.00',
      transferDate: '2026-09-01',
    });

    expect(component.form.controls.amount.valid).toBe(false);

    component.submit();

    expect(emitted).toEqual([]);
  });

  it('does not submit with a non-numeric amount', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-2',
      amount: 'abc',
      transferDate: '2026-09-01',
    });

    expect(component.form.controls.amount.valid).toBe(false);

    component.submit();

    expect(emitted).toEqual([]);
  });

  // ---- MINOR-1: a zero amount is rejected too (server: OperationAmountMustBePositiveException) ----

  it('does not submit with a zero amount ("0")', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-2',
      amount: '0',
      transferDate: '2026-09-01',
    });

    expect(component.form.controls.amount.valid).toBe(false);

    component.submit();

    expect(emitted).toEqual([]);
  });

  it('does not submit with a zero amount ("0.00")', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.setValue({
      sourceWalletId: 'wallet-1',
      targetWalletId: 'wallet-2',
      amount: '0.00',
      transferDate: '2026-09-01',
    });

    expect(component.form.controls.amount.valid).toBe(false);

    component.submit();

    expect(emitted).toEqual([]);
  });

  // ---- MAJOR-1: an empty, disabled target (no currency-compatible wallet) must not pass as valid ----

  it('keeps the form invalid, and the submit button disabled, when the source has no currency-compatible target', () => {
    component.form.controls.sourceWalletId.setValue('wallet-3'); // the only EUR wallet
    component.form.controls.amount.setValue('100.00');
    component.form.controls.transferDate.setValue('2026-09-01');
    fixture.detectChanges();

    expect(component.form.controls.targetWalletId.disabled).toBe(true);
    expect(component.form.controls.targetWalletId.value).toBe('');
    expect(component.form.invalid).toBe(true);

    const submitButton = fixture.nativeElement.querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submitButton.disabled).toBe(true);
  });

  it('does not submit when the source has no currency-compatible target, even though disabled controls are skipped by FormGroup.invalid', () => {
    const emitted: unknown[] = [];
    component.save.subscribe((value) => emitted.push(value));

    component.form.controls.sourceWalletId.setValue('wallet-3');
    component.form.controls.amount.setValue('100.00');
    component.form.controls.transferDate.setValue('2026-09-01');
    fixture.detectChanges();

    component.submit();

    expect(emitted).toEqual([]);
  });
});
