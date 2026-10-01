import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { Observable, Subject, catchError, concat, distinctUntilChanged, map, of, switchMap, tap } from 'rxjs';
import { Money, amountValidator, formatMoney } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { StatedBalanceLookupService } from '../../stated-balance-lookup.service';
import { WalletOption } from '../operation-form/operation-form.component';

/** Value emitted by {@link StatedBalanceFormComponent}: the balance at the end of `date`. */
export interface StatedBalanceFormValue {
  walletId: string;
  /** yyyy-MM-dd, within `[wallet accounting start .. today]`. */
  date: string;
  /** Decimal-point string; negative values are allowed. */
  amount: string;
}

/** State of the live hint under the date: what the operations give at the end of the chosen day. */
export type BalanceLookupState =
  | { status: 'idle' }
  | { status: 'loading' }
  | { status: 'error' }
  | { status: 'ready'; operationsBalance: Money; statedBalance: Money | null };

interface LookupKey {
  walletId: string;
  date: string;
}

/**
 * "Указать баланс на дату" form (UC-24, ADR-0003 §6): the user states the actual wallet
 * balance at the end of a day; the system records the difference with the operations as a
 * correction amount. A repeated input for the same date replaces the previous value.
 *
 * The date is limited to `[accountingStartDate of the wallet .. today]` (the server enforces
 * the same range and answers 400 otherwise).
 *
 * Live hint (ADR-0003, §7): once the wallet and the date are valid, "По операциям на конец дня: N" is shown in a polite
 * live region (stale answers are cancelled by `switchMap`; a failed lookup only replaces the hint with a notice
 * and never blocks the form). If a balance is already stated for that day, its value is put into the empty amount
 * field with a note that the new value replaces the previous one.
 */
@Component({
  selector: 'app-stated-balance-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, FieldComponent, FieldControlDirective],
  templateUrl: './stated-balance-form.component.html',
  styleUrl: './stated-balance-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StatedBalanceFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);
  private readonly lookupService = inject(StatedBalanceLookupService);

  readonly wallets = input.required<WalletOption[]>();
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<StatedBalanceFormValue>();
  readonly cancel = output<void>();

  /** Explains what happens with the entered balance; shown under the amount until an error replaces it. */
  readonly amountHint =
    'Введите фактический баланс на конец дня; разница запишется как «Неизвестные траты» или «Неизвестные доходы» ' +
    'и будет пересчитываться при изменении операций. ' +
    'Операции этой и более ранних дат не изменят указанный баланс: корректирующая сумма их поглотит. ' +
    'Повторный ввод на ту же дату заменяет прежнее значение; отменить указанный баланс нельзя';

  readonly today = this.todayIso();
  readonly selectedWalletId = signal<string | null>(null);

  private readonly selectedWallet = computed(
    () => this.wallets().find((wallet) => wallet.id === this.selectedWalletId()) ?? null,
  );
  readonly selectedWalletCurrencyCode = computed(() => this.selectedWallet()?.currencyCode ?? '');
  /** Lower bound of the date field: the accounting start of the selected wallet. */
  readonly minDate = computed(() => this.selectedWallet()?.accountingStartDate ?? null);

  readonly form = this.formBuilder.nonNullable.group({
    walletId: ['', Validators.required],
    date: [this.today, [Validators.required, (control: AbstractControl) => this.dateRangeValidator(control)]],
    amount: ['', [Validators.required, amountValidator]],
  });

  /** The live hint (a polite live region in the template). */
  readonly lookup = signal<BalanceLookupState>({ status: 'idle' });

  /** "По операциям на конец дня: N" — the balance without the correction amount of the chosen day. */
  readonly operationsBalanceText = computed(() => {
    const state = this.lookup();
    return state.status === 'ready' ? formatMoney(state.operationsBalance, this.selectedWalletCurrencyCode()) : null;
  });

  /** "На эту дату баланс уже указан: X. Новое значение заменит прежнее" — only when a balance is already stated. */
  readonly existingStatedBalanceText = computed(() => {
    const state = this.lookup();
    if (state.status !== 'ready' || !state.statedBalance) {
      return null;
    }
    const stated = formatMoney(state.statedBalance, this.selectedWalletCurrencyCode());
    return `На эту дату баланс уже указан: ${stated}. Новое значение заменит прежнее`;
  });

  private readonly lookupRequests = new Subject<LookupKey | null>();
  /** The amount put into the field by the prefill; removed again when the wallet or the date changes. */
  private prefilledAmount: string | null = null;

  constructor() {
    this.form.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((walletId) => {
      this.selectedWalletId.set(walletId || null);
      // The allowed date range depends on the wallet.
      this.form.controls.date.updateValueAndValidity();
    });

    this.form.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.requestLookup());

    this.lookupRequests
      .pipe(
        distinctUntilChanged((previous, current) => previous?.walletId === current?.walletId && previous?.date === current?.date),
        tap(() => this.clearPrefill()),
        // A new wallet/date cancels the request in flight, so a late answer never overwrites a newer one.
        switchMap((key) => (key ? this.lookupState(key) : of<BalanceLookupState>({ status: 'idle' }))),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((state) => {
        this.lookup.set(state);
        if (state.status === 'ready') {
          this.prefill(state.statedBalance);
        }
      });

    // A wallet that vanished from the list (e.g. after a 404 and a reload) must not stay selected.
    effect(() => {
      const selectedId = this.selectedWalletId();
      if (selectedId && !this.wallets().some((wallet) => wallet.id === selectedId)) {
        this.form.controls.walletId.setValue('');
      }
    });

    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  submit(): void {
    this.form.markAllAsTouched();
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }
    const value = this.form.getRawValue();
    this.save.emit({ walletId: value.walletId, date: value.date, amount: value.amount.trim() });
  }

  onCancel(): void {
    this.cancel.emit();
  }

  walletError(): string | null {
    return fieldError(this.form.controls.walletId, 'Выберите кошелек');
  }

  dateError(): string | null {
    return fieldError(this.form.controls.date, {
      required: 'Укажите дату',
      dateBeforeStart: `Дата не может быть раньше начала учёта кошелька (${this.minDate() ?? ''})`,
      dateInFuture: 'Дата не может быть позже сегодняшней',
    });
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите баланс',
      amount: 'Баланс — число с точкой в качестве разделителя (например, 1234.56)',
    });
  }

  private requestLookup(): void {
    const { walletId, date } = this.form.controls;
    const ready = !!walletId.value && walletId.valid && !!date.value && date.valid;
    this.lookupRequests.next(ready ? { walletId: walletId.value, date: date.value } : null);
  }

  private lookupState(key: LookupKey): Observable<BalanceLookupState> {
    return concat(
      of<BalanceLookupState>({ status: 'loading' }),
      this.lookupService.load(key.walletId, key.date).pipe(
        map((context): BalanceLookupState => ({ status: 'ready', ...context })),
        // A failed hint is not an error of the form: the balance can still be stated.
        catchError(() => of<BalanceLookupState>({ status: 'error' })),
      ),
    );
  }

  /** The previously stated value goes into the amount field, but never over what the user has already typed. */
  private prefill(statedBalance: Money | null): void {
    const amount = this.form.controls.amount;
    if (statedBalance && amount.value.trim() === '') {
      amount.setValue(statedBalance.amount, { emitEvent: false });
      this.prefilledAmount = statedBalance.amount;
    }
  }

  /** Removes a value put in by the prefill when it no longer belongs to the chosen wallet and date. */
  private clearPrefill(): void {
    const amount = this.form.controls.amount;
    if (this.prefilledAmount !== null && amount.value === this.prefilledAmount) {
      amount.setValue('', { emitEvent: false });
    }
    this.prefilledAmount = null;
  }

  private dateRangeValidator(control: AbstractControl): ValidationErrors | null {
    const value = control.value as string | null;
    if (!value) {
      return null;
    }
    if (value > this.todayIso()) {
      return { dateInFuture: true };
    }
    // Without a selected wallet there is no lower bound (and the required `wallets` input may not be set yet
    // when the validator runs for the first time during construction).
    if (!this.selectedWalletId()) {
      return null;
    }
    const start = this.minDate();
    return start && value < start ? { dateBeforeStart: true } : null;
  }

  private todayIso(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
