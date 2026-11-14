import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { amountValidator, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { Fund, FundWriteRequest } from '../../../../data-access/funds/funds-api.models';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/** One selectable debt or loan to link a fund with («фонд на погашение», ADR-0010 §4, В9). */
export interface FundLinkOption {
  /** `debt:<id>` or `loan:<id>`. */
  value: string;
  label: string;
}

/**
 * Fund create/edit form (#7, ADR-0010 §4): name, target amount, one currency (В6) and an optional link to a debt or
 * a loan — a reserve in such a fund is the way to set money aside for that obligation (В9). PATCH replaces every field;
 * the currency cannot change once the fund has had reserves (`hasAllocationHistory`).
 */
@Component({
  selector: 'app-fund-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, FieldComponent, FieldControlDirective, SelectComponent, SelectOptionDirective],
  templateUrl: './fund-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FundFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly currencies = input.required<Currency[]>();
  readonly linkOptions = input<FundLinkOption[]>([]);
  readonly fund = input<Fund | null>(null);
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<FundWriteRequest>();
  readonly cancel = output<void>();

  readonly isEdit = computed(() => this.fund() !== null);
  readonly currencyLocked = computed(() => this.fund()?.hasAllocationHistory ?? false);

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    targetAmount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
    currencyId: ['', Validators.required],
    link: [''],
  });

  constructor() {
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
        if (this.currencyLocked()) {
          this.form.controls.currencyId.disable({ emitEvent: false });
        }
      }
    });
  }

  ngOnInit(): void {
    const fund = this.fund();
    if (fund) {
      this.form.setValue({
        name: fund.name,
        targetAmount: fund.targetAmount.amount,
        currencyId: fund.currencyId,
        link: fund.linkedDebtId ? `debt:${fund.linkedDebtId}` : fund.linkedLoanId ? `loan:${fund.linkedLoanId}` : '',
      });
    }
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
    const [linkKind, linkId] = value.link ? value.link.split(':') : ['', ''];
    this.save.emit({
      name: value.name.trim(),
      targetAmount: value.targetAmount.trim(),
      currencyId: value.currencyId,
      linkedDebtId: linkKind === 'debt' ? linkId : null,
      linkedLoanId: linkKind === 'loan' ? linkId : null,
    });
  }

  nameError(): string | null {
    return fieldError(this.form.controls.name, 'Укажите название');
  }

  targetError(): string | null {
    return fieldError(this.form.controls.targetAmount, {
      required: 'Укажите целевую сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
    });
  }

  currencyError(): string | null {
    return fieldError(this.form.controls.currencyId, 'Выберите валюту');
  }
}
