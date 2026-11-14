import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { amountValidator, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { Creditor } from '../../../../data-access/debts/debts-api.models';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ObligationFormValue, todayIso } from '../../debts.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DateInputComponent } from '@shared/ui/date-input/date-input.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/**
 * Create/edit form shared by a debt (#6) and a loan (#8), ADR-0010 §3: creditor, name, currency, amount
 * (original amount of a debt / amount to repay of a loan) and start date. Creating a debt/loan creates no
 * operation (В3). The currency is fixed after creation (PATCH has no currency), so it is read-only when editing.
 */
@Component({
  selector: 'app-obligation-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, DateInputComponent, FieldComponent, FieldControlDirective, SelectComponent, SelectOptionDirective],
  templateUrl: './obligation-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ObligationFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly kind = input<'debt' | 'loan'>('debt');
  readonly creditors = input.required<Creditor[]>();
  readonly currencies = input.required<Currency[]>();
  /** Values of the edited record; `null` — create. */
  readonly initial = input<ObligationFormValue | null>(null);
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<ObligationFormValue>();
  readonly cancel = output<void>();
  readonly createCreditorRequested = output<void>();

  readonly isEdit = computed(() => this.initial() !== null);

  /** Active creditors, plus the current one of the edited record even if it was deactivated since. */
  readonly creditorOptions = computed(() => {
    const currentId = this.initial()?.creditorId;
    return this.creditors().filter((creditor) => creditor.isActive || creditor.id === currentId);
  });

  readonly amountLabel = computed(() => (this.kind() === 'debt' ? 'Сумма долга' : 'Сумма к погашению'));

  readonly form = this.formBuilder.nonNullable.group({
    creditorId: ['', Validators.required],
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    currencyId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
    startDate: [todayIso(), Validators.required],
  });

  constructor() {
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
        if (this.isEdit()) {
          this.form.controls.currencyId.disable({ emitEvent: false });
        }
      }
    });
  }

  ngOnInit(): void {
    const initial = this.initial();
    if (initial) {
      this.form.setValue(initial);
    }
  }

  /** Selects a creditor just created via "+ добавить кредитора". */
  selectCreditor(creditorId: string): void {
    this.form.controls.creditorId.setValue(creditorId);
    this.form.controls.creditorId.markAsDirty();
  }

  currencyLabel(currency: Currency): string {
    return `${currency.code} — ${currency.name}`;
  }

  currencyCode(): string {
    const id = this.form.controls.currencyId.value;
    return this.currencies().find((currency) => currency.id === id)?.code ?? '';
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
    this.save.emit({ ...value, name: value.name.trim(), amount: value.amount.trim() });
  }

  creditorError(): string | null {
    return fieldError(this.form.controls.creditorId, 'Выберите кредитора');
  }

  nameError(): string | null {
    return fieldError(this.form.controls.name, { required: 'Укажите название', pattern: 'Укажите название', maxlength: 'Не длиннее 200 символов' });
  }

  currencyError(): string | null {
    return fieldError(this.form.controls.currencyId, 'Выберите валюту');
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
    });
  }

  startDateError(): string | null {
    return fieldError(this.form.controls.startDate, 'Укажите дату');
  }
}
