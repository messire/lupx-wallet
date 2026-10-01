import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, Validators } from '@angular/forms';
import { amountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { WalletOption } from '../operation-form/operation-form.component';

/** Value emitted by {@link StatedBalanceFormComponent}: the balance at the end of `date`. */
export interface StatedBalanceFormValue {
  walletId: string;
  /** yyyy-MM-dd, within `[wallet accounting start .. today]`. */
  date: string;
  /** Decimal-point string; negative values are allowed. */
  amount: string;
}

/**
 * "Указать баланс на дату" form (UC-24, ADR-0003 §6): the user states the actual wallet
 * balance at the end of a day; the system records the difference with the operations as a
 * correction amount. A repeated input for the same date replaces the previous value.
 *
 * The date is limited to `[accountingStartDate of the wallet .. today]` (the server enforces
 * the same range and answers 400 otherwise).
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

  readonly wallets = input.required<WalletOption[]>();
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<StatedBalanceFormValue>();
  readonly cancel = output<void>();

  /** Explains what happens with the entered balance; shown under the amount until an error replaces it. */
  readonly amountHint =
    'Введите фактический баланс на конец дня; разница запишется как «Неизвестные траты» или «Неизвестные доходы» ' +
    'и будет пересчитываться при изменении операций';

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

  constructor() {
    this.form.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((walletId) => {
      this.selectedWalletId.set(walletId || null);
      // The allowed date range depends on the wallet.
      this.form.controls.date.updateValueAndValidity();
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
