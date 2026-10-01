import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { environment } from '../../../../../environments/environment';
import { Operation } from '../../../../data-access/operations/operations-api.models';
import { StatedBalanceFormComponent, StatedBalanceFormValue } from './stated-balance-form.component';

describe('StatedBalanceFormComponent', () => {
  let fixture: ComponentFixture<StatedBalanceFormComponent>;
  let component: StatedBalanceFormComponent;
  let httpMock: HttpTestingController;

  const wallets = [
    { id: 'wallet-1', name: 'Наличные', currencyCode: 'USD', accountingStartDate: '2026-01-01' },
    { id: 'wallet-2', name: 'Карта', currencyCode: 'EUR', accountingStartDate: '2026-03-01' },
  ];

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [StatedBalanceFormComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();
    httpMock = TestBed.inject(HttpTestingController);

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

  it('names the consequences in the amount hint: replacement, no way to cancel, absorbed operations of that date', () => {
    const hint = (fixture.nativeElement as HTMLElement).querySelector('.app-field__hint')?.textContent ?? '';

    expect(hint).toContain('Повторный ввод на ту же дату заменяет прежнее значение');
    expect(hint).toContain('отменить указанный баланс нельзя');
    expect(hint).toContain('Операции этой и более ранних дат не изменят указанный баланс: корректирующая сумма их поглотит');
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

  // ---- Date range [accountingStartDate .. today] ----

  const root = () => fixture.nativeElement as HTMLElement;
  const amountInput = () => root().querySelector('input[inputmode="decimal"]') as HTMLInputElement;

  /** Shifts a yyyy-MM-dd date by whole days (UTC, like the component's own "today"). */
  function shiftDate(date: string, days: number): string {
    const shifted = new Date(`${date}T00:00:00Z`);
    shifted.setUTCDate(shifted.getUTCDate() + days);
    return shifted.toISOString().slice(0, 10);
  }

  it('accepts both ends of the range: the wallet accounting start date and today', () => {
    component.form.controls.walletId.setValue('wallet-2');

    component.form.controls.date.setValue('2026-03-01');
    expect(component.form.controls.date.valid).toBe(true);

    component.form.controls.date.setValue(component.today);
    expect(component.form.controls.date.valid).toBe(true);
  });

  it('rejects the day before the accounting start and the day after today', () => {
    component.form.controls.walletId.setValue('wallet-2');

    component.form.controls.date.setValue('2026-02-28');
    expect(component.form.controls.date.errors).toEqual({ dateBeforeStart: true });

    component.form.controls.date.setValue(shiftDate(component.today, 1));
    expect(component.form.controls.date.errors).toEqual({ dateInFuture: true });
  });

  it('has no lower bound before a wallet is chosen, but the upper bound (today) always applies', () => {
    component.form.controls.date.setValue('1999-01-01');
    expect(component.form.controls.date.valid).toBe(true);
    expect(component.minDate()).toBeNull();
    fixture.detectChanges();
    expect((root().querySelector('input[type="date"]') as HTMLInputElement).hasAttribute('min')).toBe(false);

    component.form.controls.date.setValue(shiftDate(component.today, 1));
    expect(component.form.controls.date.hasError('dateInFuture')).toBe(true);
  });

  it('recalculates the range in both directions when the wallet changes', () => {
    component.form.patchValue({ walletId: 'wallet-1', date: '2026-02-01' });
    expect(component.form.controls.date.valid).toBe(true);

    component.form.controls.walletId.setValue('wallet-2');
    expect(component.form.controls.date.hasError('dateBeforeStart')).toBe(true);
    expect(component.minDate()).toBe('2026-03-01');

    component.form.controls.walletId.setValue('wallet-1');
    expect(component.form.controls.date.valid).toBe(true);
    expect(component.minDate()).toBe('2026-01-01');
  });

  it('shows the date error with the accounting start of the chosen wallet next to the date field', () => {
    component.form.setValue({ walletId: 'wallet-2', date: '2026-02-28', amount: '1' });
    component.submit();
    fixture.detectChanges();

    const error = root().querySelector('input[type="date"]')?.closest('app-field')?.querySelector('.app-field__error');
    expect(error?.textContent).toContain('начала учёта кошелька (2026-03-01)');
    expect(error?.getAttribute('role')).toBe('alert');
  });

  it('does not emit anything for an out-of-range date even if the other fields are valid', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-2', date: shiftDate(component.today, 1), amount: '10' });

    component.submit();

    expect(values).toEqual([]);
  });

  // ---- Amount: a decimal string, the sign is allowed ----

  it('accepts negative, zero and decimal balances and emits them as the entered strings', () => {
    const values = emitted();
    for (const amount of ['-0.5', '0', '-1500', '1234.56', '0.123456789012345678']) {
      component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount });
      component.submit();
    }

    expect(values.map((value) => value.amount)).toEqual(['-0.5', '0', '-1500', '1234.56', '0.123456789012345678']);
  });

  it('trims the whitespace around the amount before emitting it', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount: '  350.00 ' });

    component.submit();

    expect(values).toEqual([{ walletId: 'wallet-1', date: '2026-09-10', amount: '350.00' }]);
  });

  it('rejects a comma as the decimal separator (like the other money forms) and explains the expected format', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount: '350,50' });

    component.submit();

    expect(values).toEqual([]);
    expect(component.form.controls.amount.hasError('amount')).toBe(true);
    expect(component.amountError()).toBe('Баланс — число с точкой в качестве разделителя (например, 1234.56)');
  });

  it('rejects malformed amounts', () => {
    const values = emitted();
    for (const amount of ['abc', '1e3', '1.', '.5', '--5', '+5', '1 000', '1.2.3']) {
      component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount });
      component.submit();
      expect(component.form.controls.amount.hasError('amount'), `amount "${amount}"`).toBe(true);
    }

    expect(values).toEqual([]);
  });

  it('requires the amount and says so', () => {
    const values = emitted();
    component.form.patchValue({ walletId: 'wallet-1' });

    component.submit();

    expect(values).toEqual([]);
    expect(component.amountError()).toBe('Укажите баланс');
  });

  // ---- Accessibility: hint and error through aria-describedby ----

  it('points aria-describedby of the amount at the error while it is shown, and back at the hint afterwards', () => {
    component.form.patchValue({ walletId: 'wallet-1' });
    component.submit();
    fixture.detectChanges();

    const field = amountInput().closest('app-field') as HTMLElement;
    const error = field.querySelector('.app-field__error') as HTMLElement;
    expect(error.textContent).toContain('Укажите баланс');
    expect(error.getAttribute('role')).toBe('alert');
    expect(amountInput().getAttribute('aria-describedby')).toBe(error.id);
    expect(amountInput().getAttribute('aria-invalid')).toBe('true');
    expect(field.querySelector('.app-field__hint')).toBeNull();

    component.form.controls.amount.setValue('350');
    fixture.detectChanges();

    const hint = field.querySelector('.app-field__hint') as HTMLElement;
    expect(hint.textContent).toContain('Введите фактический баланс на конец дня');
    expect(amountInput().getAttribute('aria-describedby')).toBe(hint.id);
    expect(amountInput().hasAttribute('aria-invalid')).toBe(false);
  });

  it('marks the required fields and links every field label to its control', () => {
    const labels = Array.from(root().querySelectorAll('label.app-field__label')) as HTMLLabelElement[];

    expect(labels.map((label) => label.textContent?.replace('*', '').trim())).toEqual([
      'Кошелек',
      'Дата',
      'Баланс на конец дня',
    ]);
    for (const label of labels) {
      expect(label.querySelector('.app-field__required')?.getAttribute('aria-hidden')).toBe('true');
      expect(root().querySelector(`#${label.htmlFor}`), `control of "${label.textContent}"`).not.toBeNull();
    }
  });

  // ---- Submission through the DOM ----

  it('submits what the user typed: the chosen wallet, the date and the signed amount', () => {
    const values = emitted();
    const select = root().querySelector('select') as HTMLSelectElement;
    select.value = 'wallet-2';
    select.dispatchEvent(new Event('change'));
    const date = root().querySelector('input[type="date"]') as HTMLInputElement;
    date.value = '2026-04-15';
    date.dispatchEvent(new Event('input'));
    amountInput().value = '-120.75';
    amountInput().dispatchEvent(new Event('input'));
    fixture.detectChanges();

    (root().querySelector('button[type="submit"]') as HTMLButtonElement).click();

    expect(values).toEqual([{ walletId: 'wallet-2', date: '2026-04-15', amount: '-120.75' }]);
  });

  // ---- Blocking while the request is in flight ----

  it('disables every field and the submit button and shows the progress text while the request is in flight', () => {
    fixture.componentRef.setInput('isSubmitting', true);
    fixture.detectChanges();

    for (const control of Array.from(root().querySelectorAll('select, input'))) {
      expect((control as HTMLInputElement).disabled, control.outerHTML).toBe(true);
    }
    const submit = root().querySelector('button[type="submit"]') as HTMLButtonElement;
    expect(submit.disabled).toBe(true);
    expect(submit.getAttribute('aria-busy')).toBe('true');
    expect(submit.textContent).toContain('Сохранение…');
  });

  it('does not emit on a submit click while the request is in flight', () => {
    const values = emitted();
    component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount: '10' });
    fixture.componentRef.setInput('isSubmitting', true);
    fixture.detectChanges();

    (root().querySelector('button[type="submit"]') as HTMLButtonElement).click();
    root().querySelector('form')?.dispatchEvent(new Event('submit'));

    expect(values).toEqual([]);
  });

  it('unblocks the fields again with the entered values intact when the request fails', () => {
    component.form.setValue({ walletId: 'wallet-1', date: '2026-09-10', amount: '350' });
    fixture.componentRef.setInput('isSubmitting', true);
    fixture.detectChanges();

    fixture.componentRef.setInput('isSubmitting', false);
    fixture.componentRef.setInput('errorMessage', 'Запись изменена параллельно.');
    fixture.detectChanges();

    expect(component.form.enabled).toBe(true);
    expect((root().querySelector('select') as HTMLSelectElement).disabled).toBe(false);
    expect(amountInput().disabled).toBe(false);
    expect(amountInput().value).toBe('350');
    expect(root().querySelector('p.form-error[role="alert"]')?.textContent).toContain('Запись изменена параллельно.');
    expect((root().querySelector('button[type="submit"]') as HTMLButtonElement).disabled).toBe(false);
  });

  it('disables the cancel button while the request is in flight and enables it again afterwards', () => {
    fixture.componentRef.setInput('isSubmitting', true);
    fixture.detectChanges();

    const cancel = () => {
      const buttons = root().querySelectorAll('button[type="button"]');
      return buttons[buttons.length - 1] as HTMLButtonElement;
    };
    expect(cancel().textContent).toContain('Отмена');
    expect(cancel().disabled).toBe(true);

    fixture.componentRef.setInput('isSubmitting', false);
    fixture.detectChanges();
    expect(cancel().disabled).toBe(false);
  });

  // ---- Live hint "По операциям на конец дня" and the prefill of a stated balance ----

  const api = environment.apiBaseUrl;
  const liveRegion = () => root().querySelector('.balance-lookup') as HTMLElement;
  const liveText = () => (liveRegion().textContent ?? '').replace(/\s+/g, ' ').trim();

  const correction = (date: string, amount: string, stated: string | null): Operation => ({
    id: 'corr-1',
    walletId: 'wallet-1',
    operationTypeId: null,
    amount: { amount, currencyId: 'c-usd' },
    operationDate: date,
    adjustmentMode: null,
    isCorrection: true,
    statedBalance: stated === null ? null : { amount: stated, currencyId: 'c-usd' },
    transferId: null,
    isDeleted: false,
    deletedAt: null,
    createdAt: '2026-09-10T00:00:00Z',
    updatedAt: '2026-09-10T00:00:00Z',
  });

  const regularOperation = (): Operation => ({
    ...correction('2026-09-10', '10', null),
    id: 'op-1',
    isCorrection: false,
    operationTypeId: 'ot-1',
  });

  const balanceRequest = (walletId: string, date: string): TestRequest =>
    httpMock.expectOne((r) => r.url === `${api}/wallets/${walletId}/balance` && r.params.get('date') === date);

  const operationsRequest = (walletId: string, date: string): TestRequest =>
    httpMock.expectOne(
      (r) =>
        r.url === `${api}/operations` &&
        r.params.get('walletId') === walletId &&
        r.params.get('dateFrom') === date &&
        r.params.get('dateTo') === date,
    );

  /** Answers both requests of the lookup: the balance of the day (with the correction included) and the day's operations. */
  function answerLookup(walletId: string, date: string, balance: string, operations: Operation[] = []): void {
    balanceRequest(walletId, date).flush({ walletId, date, balance: { amount: balance, currencyId: 'c-usd' } });
    operationsRequest(walletId, date).flush({ data: operations, pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();
  }

  it('has an empty polite live region from the start, so a screen reader announces the hint when it appears', () => {
    expect(liveRegion().getAttribute('aria-live')).toBe('polite');
    expect(liveRegion().getAttribute('aria-atomic')).toBe('true');
    expect(liveText()).toBe('');
    httpMock.expectNone((r) => r.url.includes('/balance'));
  });

  it('asks for the balance and the operations of the chosen day once the wallet and the date are valid', () => {
    component.form.controls.date.setValue('2026-09-10');
    httpMock.expectNone((r) => r.url.includes('/balance')); // no wallet yet

    component.form.controls.walletId.setValue('wallet-1');

    const balance = balanceRequest('wallet-1', '2026-09-10');
    const operations = operationsRequest('wallet-1', '2026-09-10');
    expect(balance.request.method).toBe('GET');
    expect(operations.request.params.get('limit')).toBe('100');
  });

  it('shows the loading text, then "По операциям на конец дня: N" with the wallet currency', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    fixture.detectChanges();
    expect(liveText()).toBe('Считаем баланс по операциям…');

    answerLookup('wallet-1', '2026-09-10', '1350.00');

    expect(liveText()).toBe('По операциям на конец дня: 1 350.00 USD');
    expect(liveRegion().querySelector('[role="alert"]')).toBeNull();
  });

  it('subtracts the correction amount of that very day: the endpoint includes it, the hint must not', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');

    // 300.00 by the operations + 50.00 of the correction = 350.00 stated.
    answerLookup('wallet-1', '2026-09-10', '350.00', [regularOperation(), correction('2026-09-10', '50.00', '350.00')]);

    expect(liveText()).toContain('По операциям на конец дня: 300.00 USD');
  });

  it('subtracts a negative correction amount (the sign flips the arithmetic, not the text)', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');

    answerLookup('wallet-1', '2026-09-10', '350.00', [correction('2026-09-10', '-50.00', '350.00')]);

    expect(liveText()).toContain('По операциям на конец дня: 400.00 USD');
  });

  it('subtracts nothing when the day has no correction amount, and shows no "already stated" note or prefill', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');

    answerLookup('wallet-1', '2026-09-10', '300.00', [regularOperation()]);

    expect(liveText()).toBe('По операциям на конец дня: 300.00 USD');
    expect(amountInput().value).toBe('');
  });

  it('prefills the previously stated value and says the new value replaces it', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');

    answerLookup('wallet-1', '2026-09-10', '350.00', [correction('2026-09-10', '50.00', '350.00')]);

    expect(amountInput().value).toBe('350.00');
    expect(component.form.controls.amount.valid).toBe(true);
    expect(liveText()).toContain('На эту дату баланс уже указан: 350.00 USD. Новое значение заменит прежнее');
  });

  it('never overwrites what the user has already typed, but still tells that a balance is stated', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    component.form.controls.amount.setValue('123');

    answerLookup('wallet-1', '2026-09-10', '350.00', [correction('2026-09-10', '50.00', '350.00')]);

    expect(amountInput().value).toBe('123');
    expect(liveText()).toContain('На эту дату баланс уже указан: 350.00 USD');
  });

  it('removes a prefilled value when the date changes to a day without a stated balance, and keeps a typed one', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    answerLookup('wallet-1', '2026-09-10', '350.00', [correction('2026-09-10', '50.00', '350.00')]);
    expect(component.form.controls.amount.value).toBe('350.00');

    component.form.controls.date.setValue('2026-09-11');
    expect(component.form.controls.amount.value).toBe('');
    answerLookup('wallet-1', '2026-09-11', '300.00');
    expect(component.form.controls.amount.value).toBe('');
    expect(liveText()).toBe('По операциям на конец дня: 300.00 USD');

    // A value typed by the user survives the next change of the date.
    component.form.controls.amount.setValue('777');
    component.form.controls.date.setValue('2026-09-12');
    answerLookup('wallet-1', '2026-09-12', '300.00');
    expect(component.form.controls.amount.value).toBe('777');
  });

  it('cancels the request in flight when the date changes: a stale answer never reaches the hint', () => {
    component.form.controls.walletId.setValue('wallet-1'); // the date is today
    const staleBalance = httpMock.expectOne((r) => r.url === `${api}/wallets/wallet-1/balance`);
    const staleOperations = httpMock.expectOne((r) => r.url === `${api}/operations`);

    component.form.controls.date.setValue('2026-09-10');
    expect(staleBalance.cancelled).toBe(true);
    expect(staleOperations.cancelled).toBe(true);

    answerLookup('wallet-1', '2026-09-10', '111.00');
    expect(liveText()).toBe('По операциям на конец дня: 111.00 USD');
  });

  it('does not repeat the request when only the amount changes', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    answerLookup('wallet-1', '2026-09-10', '100');

    component.form.controls.amount.setValue('5');
    component.form.controls.amount.setValue('50');

    httpMock.expectNone((r) => r.url.includes('/balance') || r.url === `${api}/operations`);
  });

  it('shows no hint and sends nothing for a date outside the wallet range, and clears a hint that was shown', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-2');
    answerLookup('wallet-2', '2026-09-10', '100');
    expect(liveText()).toContain('По операциям на конец дня');

    component.form.controls.date.setValue('2026-02-01'); // before the accounting start of wallet-2
    fixture.detectChanges();

    expect(liveText()).toBe('');
    httpMock.expectNone((r) => r.url.includes('/balance'));
  });

  it('asks again for the other wallet when the wallet changes and shows its currency', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    answerLookup('wallet-1', '2026-09-10', '100');

    component.form.controls.walletId.setValue('wallet-2');
    answerLookup('wallet-2', '2026-09-10', '200');

    expect(liveText()).toBe('По операциям на конец дня: 200 EUR');
  });

  it('follows the cursor of the day list: the correction amount comes last and may be on a later page', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');

    balanceRequest('wallet-1', '2026-09-10').flush({
      walletId: 'wallet-1',
      date: '2026-09-10',
      balance: { amount: '350.00', currencyId: 'c-usd' },
    });
    operationsRequest('wallet-1', '2026-09-10').flush({
      data: [regularOperation()],
      pagination: { nextCursor: 'cursor-2', hasMore: true },
    });
    const next = httpMock.expectOne((r) => r.url === `${api}/operations` && r.params.get('cursor') === 'cursor-2');
    next.flush({ data: [correction('2026-09-10', '50.00', '350.00')], pagination: { nextCursor: null, hasMore: false } });
    fixture.detectChanges();

    expect(liveText()).toContain('По операциям на конец дня: 300.00 USD');
    expect(amountInput().value).toBe('350.00');
  });

  it('shows a calm notice when the lookup fails and does not block the form', () => {
    const values = emitted();
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    operationsRequest('wallet-1', '2026-09-10').flush({ data: [], pagination: { nextCursor: null, hasMore: false } });
    balanceRequest('wallet-1', '2026-09-10').flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();

    expect(liveText()).toContain('Не удалось получить баланс по операциям на эту дату');
    expect(liveRegion().querySelector('[role="alert"]')).toBeNull();
    expect(component.form.disabled).toBe(false);

    component.form.controls.amount.setValue('350');
    component.submit();
    expect(values).toEqual([{ walletId: 'wallet-1', date: '2026-09-10', amount: '350' }]);
  });

  it('recovers from a failed lookup when the date changes', () => {
    component.form.controls.date.setValue('2026-09-10');
    component.form.controls.walletId.setValue('wallet-1');
    balanceRequest('wallet-1', '2026-09-10').flush(null, { status: 500, statusText: 'Server Error' });
    fixture.detectChanges();
    expect(liveText()).toContain('Не удалось получить баланс');
    // The operations request of the failed lookup was cancelled together with it.
    expect(operationsRequest('wallet-1', '2026-09-10').cancelled).toBe(true);

    component.form.controls.date.setValue('2026-09-11');
    answerLookup('wallet-1', '2026-09-11', '10');

    expect(liveText()).toBe('По операциям на конец дня: 10 USD');
  });

  it('drops a wallet that disappeared from the list (e.g. after a 404 and a reload) from the selection', () => {
    component.form.controls.walletId.setValue('wallet-2');
    fixture.detectChanges();
    expect(component.form.controls.walletId.value).toBe('wallet-2');

    fixture.componentRef.setInput('wallets', [wallets[0]]);
    fixture.detectChanges();

    expect(component.form.controls.walletId.value).toBe('');
    expect(component.selectedWalletCurrencyCode()).toBe('');
    expect(liveText()).toBe('');
  });
});
