import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator, compareAmounts, formatAmount, isValidAmount, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { PlannedOccurrence, PlannedOccurrenceExecuteRequest } from '../../../../data-access/planning/planning-api.models';
import { todayIsoDate } from '@shared/ui/date-input/calendar-date';
import { PlanWalletOption } from '../../planning.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DateInputComponent } from '@shared/ui/date-input/date-input.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/**
 * Executes a planned occurrence (#9, ADR-0010 §6): a regular income/expense operation is created in the same server
 * transaction and linked with the occurrence. The form is prefilled from the plan — its wallet, the remaining expected
 * amount and the occurrence's effective date (today if that is still in the future: an operation cannot be dated after
 * today). A smaller amount is a partial execution — the rest stays expected; a larger one is allowed.
 */
@Component({
  selector: 'app-occurrence-execute-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, DateInputComponent, FieldComponent, FieldControlDirective, SelectComponent, SelectOptionDirective],
  templateUrl: './occurrence-execute-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OccurrenceExecuteFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly occurrence = input.required<PlannedOccurrence>();
  /** Wallets in the plan currency (not archived). */
  readonly wallets = input.required<PlanWalletOption[]>();
  readonly currencyCode = input('');
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<PlannedOccurrenceExecuteRequest>();
  readonly cancel = output<void>();

  readonly maxDate = todayIsoDate();
  private readonly walletId = signal('');
  readonly minDate = computed(() => this.wallets().find((wallet) => wallet.id === this.walletId())?.accountingStartDate ?? null);

  private readonly dateRange: ValidatorFn = (control): ValidationErrors | null => {
    const value = control.value as string;
    if (!value) {
      return null;
    }
    const min = this.minDate();
    if (min && value < min) {
      return { tooEarly: true };
    }
    return value > this.maxDate ? { tooLate: true } : null;
  };

  readonly form = this.formBuilder.nonNullable.group({
    walletId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
    operationDate: ['', Validators.required],
  });

  constructor() {
    this.form.controls.operationDate.addValidators(this.dateRange);
    this.form.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.walletId.set(id));
    effect(() => {
      this.minDate();
      this.form.controls.operationDate.updateValueAndValidity({ emitEvent: false });
    });
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  ngOnInit(): void {
    const occurrence = this.occurrence();
    const wallets = this.wallets();
    const planWallet = wallets.find((wallet) => wallet.id === occurrence.walletId);
    this.form.setValue({
      walletId: planWallet?.id ?? (wallets.length === 1 ? wallets[0].id : ''),
      amount: occurrence.expectedAmount.amount,
      operationDate: occurrence.effectiveDate > this.maxDate ? this.maxDate : occurrence.effectiveDate,
    });
  }

  /** Live note: partial execution leaves the rest expected; more than expected is allowed. */
  amountNote(): string | null {
    const value = this.form.controls.amount.value.trim();
    const expected = this.occurrence().expectedAmount.amount;
    if (!value || !isValidAmount(value)) {
      return null;
    }
    const comparison = compareAmounts(value, expected);
    if (comparison < 0) {
      return `Частичное исполнение: остаток ${formatAmount(expected)} ${this.currencyCode()} уменьшится, наступление останется ожидаемым`;
    }
    return comparison > 0 ? 'Сумма больше ожидаемой — это допустимо' : null;
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.save.emit({ walletId: value.walletId, amount: value.amount.trim(), operationDate: value.operationDate });
  }

  walletError(): string | null {
    return fieldError(this.form.controls.walletId, 'Выберите кошелёк');
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
    });
  }

  dateError(): string | null {
    return fieldError(this.form.controls.operationDate, {
      required: 'Укажите дату',
      tooEarly: `Дата не может быть раньше ${this.minDate()} — начала учёта кошелька`,
      tooLate: `Дата не может быть позже ${this.maxDate}`,
    });
  }
}
