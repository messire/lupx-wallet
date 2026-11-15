import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator, compareAmounts, formatAmount, isValidAmount, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { PaymentCreateRequest } from '../../../../data-access/debts/debts-api.models';
import { OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { PaymentWalletOption, todayIso } from '../../debts.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DateInputComponent } from '@shared/ui/date-input/date-input.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/**
 * Repayment of a debt / payment of a loan (ADR-0010, В4): the server creates a regular expense operation from the
 * chosen wallet (in the debt/loan currency) with the chosen expense operation type and links it with the obligation.
 * Client-side checks mirror the contract (amount > 0 and ≤ remaining, date ≤ today and not before the wallet's
 * accounting start); the server stays the source of truth and its error is shown under the form.
 */
@Component({
  selector: 'app-payment-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, DateInputComponent, FieldComponent, FieldControlDirective, SelectComponent, SelectOptionDirective],
  templateUrl: './payment-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PaymentFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  /** Wallets in the obligation currency, already filtered by the page. */
  readonly wallets = input.required<PaymentWalletOption[]>();
  /** Active expense operation types. */
  readonly operationTypes = input.required<OperationType[]>();
  readonly remainingAmount = input.required<string>();
  readonly currencyCode = input('');
  /** Prefilled amount (e.g. the next loan payment); empty — the user types it. */
  readonly defaultAmount = input<string | null>(null);
  readonly submitLabel = input('Погасить');
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<PaymentCreateRequest>();
  readonly cancel = output<void>();

  private readonly walletId = signal('');
  readonly maxDate = todayIso();
  readonly minDate = computed(() => this.wallets().find((wallet) => wallet.id === this.walletId())?.accountingStartDate ?? null);

  private readonly notMoreThanRemaining: ValidatorFn = (control): ValidationErrors | null => {
    const value = String(control.value ?? '').trim();
    if (!value || !isValidAmount(value)) {
      return null;
    }
    return compareAmounts(value, this.remainingAmount()) > 0 ? { moreThanRemaining: true } : null;
  };

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
    operationTypeId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
    paymentDate: [todayIso(), Validators.required],
  });

  constructor() {
    // Attached after construction: the validators read required inputs, which are not set during field init (NG0950).
    this.form.controls.amount.addValidators(this.notMoreThanRemaining);
    this.form.controls.paymentDate.addValidators(this.dateRange);
    this.form.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.walletId.set(id));

    effect(() => {
      this.minDate();
      this.remainingAmount();
      this.form.controls.paymentDate.updateValueAndValidity({ emitEvent: false });
      this.form.controls.amount.updateValueAndValidity({ emitEvent: false });
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
    const wallets = this.wallets();
    if (wallets.length === 1) {
      this.form.controls.walletId.setValue(wallets[0].id);
    }
    const types = this.operationTypes();
    if (types.length === 1) {
      this.form.controls.operationTypeId.setValue(types[0].id);
    }
    const amount = this.defaultAmount();
    if (amount) {
      this.form.controls.amount.setValue(amount);
    }
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
    this.save.emit({ ...value, amount: value.amount.trim() });
  }

  remainingLabel(): string {
    return `Остаток: ${formatAmount(this.remainingAmount())} ${this.currencyCode()}`.trim();
  }

  walletError(): string | null {
    return fieldError(this.form.controls.walletId, 'Выберите кошелёк');
  }

  walletHint(): string | null {
    return this.wallets().length === 0 ? `Нет кошельков в валюте ${this.currencyCode()} — создайте кошелёк в этой валюте` : null;
  }

  typeError(): string | null {
    return fieldError(this.form.controls.operationTypeId, 'Выберите тип расхода');
  }

  typeHint(): string | null {
    return this.operationTypes().length === 0 ? 'Нет активных типов расхода — добавьте тип в «Справочниках»' : 'Погашение будет учтено как обычный расход этого типа';
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
      moreThanRemaining: `Сумма не может превышать остаток (${formatAmount(this.remainingAmount())} ${this.currencyCode()})`,
    });
  }

  dateError(): string | null {
    return fieldError(this.form.controls.paymentDate, {
      required: 'Укажите дату',
      tooEarly: `Дата не может быть раньше ${this.minDate()} — начала учёта кошелька`,
      tooLate: `Дата не может быть позже ${this.maxDate}`,
    });
  }
}
