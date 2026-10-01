import { ComponentFixture, TestBed } from '@angular/core/testing';
import { StatedBalanceFormComponent, StatedBalanceFormValue } from './stated-balance-form.component';

describe('StatedBalanceFormComponent', () => {
  let fixture: ComponentFixture<StatedBalanceFormComponent>;
  let component: StatedBalanceFormComponent;

  const wallets = [
    { id: 'wallet-1', name: 'Наличные', currencyCode: 'USD', accountingStartDate: '2026-01-01' },
    { id: 'wallet-2', name: 'Карта', currencyCode: 'EUR', accountingStartDate: '2026-03-01' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [StatedBalanceFormComponent] }).compileComponents();

    fixture = TestBed.createComponent(StatedBalanceFormComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('wallets', wallets);
    fixture.detectChanges();
  });

  function emitted(): StatedBalanceFormValue[] {
    const values: StatedBalanceFormValue[] = [];
    component.save.subscribe((value) => values.push(value));
    return values;
  }

  it('defaults the date to today and describes the correction in the amount hint', () => {
    expect(component.form.controls.date.value).toBe(new Date().toISOString().slice(0, 10));
    const text = (fixture.nativeElement as HTMLElement).textContent ?? '';
    expect(text).toContain('Введите фактический баланс на конец дня');
    expect(text).toContain('Неизвестные траты');
    expect(text).toContain('будет пересчитываться при изменении операций');
  });

  it('links the amount hint to the control through aria-describedby', () => {
    const input = (fixture.nativeElement as HTMLElement).querySelector('input[inputmode="decimal"]') as HTMLInputElement;
    const hint = (fixture.nativeElement as HTMLElement).querySelector('.app-field__hint') as HTMLElement;

    expect(hint.id).toBeTruthy();
    expect(input.getAttribute('aria-describedby')).toBe(hint.id);
  });

  it('emits walletId, date and amount (negative allowed) on submit', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount: '-350.00' });

    component.submit();

    expect(values).toEqual([{ walletId: 'wallet-1', date: '2026-09-10', amount: '-350.00' }]);
  });

  it('does not submit an invalid amount or a missing wallet, and marks the fields as touched', () => {
    const values = emitted();
    component.form.patchValue({ amount: 'abc' });

    component.submit();

    expect(values).toEqual([]);
    expect(component.walletError()).toBe('Выберите кошелек');
    expect(component.amountError()).toContain('число с точкой');
  });

  it('rejects a date before the wallet accounting start and shows the start date', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-2', date: '2026-02-28', amount: '1' });

    component.submit();

    expect(values).toEqual([]);
    expect(component.dateError()).toContain('2026-03-01');
  });

  it('rejects a date after today', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-1', date: '2999-01-01', amount: '1' });

    component.submit();

    expect(values).toEqual([]);
    expect(component.dateError()).toBe('Дата не может быть позже сегодняшней');
  });

  it('revalidates the date when the wallet changes and limits the date input by the wallet start and today', () => {
    component.form.patchValue({ walletId: 'wallet-1', date: '2026-02-01' });
    expect(component.form.controls.date.valid).toBe(true);

    component.form.controls.walletId.setValue('wallet-2');
    fixture.detectChanges();

    expect(component.form.controls.date.hasError('dateBeforeStart')).toBe(true);
    const date = (fixture.nativeElement as HTMLElement).querySelector('input[type="date"]') as HTMLInputElement;
    expect(date.getAttribute('min')).toBe('2026-03-01');
    expect(date.getAttribute('max')).toBe(component.today);
  });

  it('shows the wallet currency next to the amount', () => {
    component.form.controls.walletId.setValue('wallet-2');
    fixture.detectChanges();

    expect((fixture.nativeElement as HTMLElement).querySelector('.input-suffix__code')?.textContent).toBe('EUR');
  });

  it('shows the server error as an alert and blocks the fields while submitting', () => {
    fixture.componentRef.setInput('errorMessage', 'Дата вне диапазона.');
    fixture.componentRef.setInput('isSubmitting', true);
    fixture.detectChanges();

    const alert = (fixture.nativeElement as HTMLElement).querySelector('p.form-error[role="alert"]');
    expect(alert?.textContent).toContain('Дата вне диапазона.');
    expect(component.form.disabled).toBe(true);

    const values = emitted();
    component.submit();
    expect(values).toEqual([]);
  });

  it('emits cancel', () => {
    let cancelled = 0;
    component.cancel.subscribe(() => cancelled++);

    component.onCancel();

    expect(cancelled).toBe(1);
  });
});
