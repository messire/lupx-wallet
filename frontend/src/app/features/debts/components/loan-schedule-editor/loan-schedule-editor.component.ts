import { ChangeDetectionStrategy, Component, OnInit, effect, inject, input, output } from '@angular/core';
import { FormArray, FormBuilder, FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { addAmounts, amountValidator, formatAmount, isZeroAmount, positiveAmountValidator, subtractAmounts } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { Loan, LoanScheduleGenerateRequest, LoanScheduleItemInput } from '../../../../data-access/debts/debts-api.models';
import { todayIso } from '../../debts.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DateInputComponent } from '@shared/ui/date-input/date-input.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

type ScheduleRow = FormGroup<{ dueDate: FormControl<string>; amount: FormControl<string> }>;

/**
 * Loan schedule editor (#8, ADR-0010 §3): either generate N equal monthly payments (POST …/schedule/generate —
 * the server splits the amount and moves missing days to the month end) or edit the items by hand and replace the
 * whole schedule (PUT …/schedule: move a date, change amounts, add/remove items). Both replace the current schedule;
 * payments already made are re-matched by the server to the earliest unpaid items.
 */
@Component({
  selector: 'app-loan-schedule-editor',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, DateInputComponent, FieldComponent, FieldControlDirective],
  templateUrl: './loan-schedule-editor.component.html',
  styleUrl: './loan-schedule-editor.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoanScheduleEditorComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);

  readonly loan = input.required<Loan>();
  readonly currencyCode = input('');
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly replace = output<LoanScheduleItemInput[]>();
  readonly generate = output<LoanScheduleGenerateRequest>();
  readonly cancel = output<void>();

  readonly generateForm = this.formBuilder.nonNullable.group({
    firstPaymentDate: [todayIso(), Validators.required],
    numberOfPayments: [12, [Validators.required, Validators.min(1), Validators.max(600), Validators.pattern(/^\d+$/)]],
  });

  readonly rows = new FormArray<ScheduleRow>([]);

  constructor() {
    effect(() => {
      const disabled = this.isSubmitting();
      for (const control of [this.generateForm, this.rows]) {
        if (disabled) {
          control.disable({ emitEvent: false });
        } else {
          control.enable({ emitEvent: false });
        }
      }
    });
  }

  ngOnInit(): void {
    for (const item of this.loan().schedule) {
      this.rows.push(this.createRow(item.dueDate, item.amount.amount));
    }
  }

  addRow(): void {
    const last = this.rows.at(this.rows.length - 1)?.controls.dueDate.value;
    this.rows.push(this.createRow(last ?? todayIso(), ''));
  }

  removeRow(index: number): void {
    this.rows.removeAt(index);
    this.rows.markAsDirty();
  }

  /** Sum of the edited items (string-exact) and its difference from the amount to repay — shown live under the list. */
  scheduleTotal(): string {
    return this.rows.controls.reduce((total, row) => {
      const amount = row.controls.amount.value.trim();
      return row.controls.amount.valid && amount ? addAmounts(total, amount) : total;
    }, '0');
  }

  totalHint(): string {
    const total = this.scheduleTotal();
    const target = this.loan().amountToRepay.amount;
    const difference = subtractAmounts(target, total);
    const code = this.currencyCode();
    const base = `Сумма графика: ${formatAmount(total)} ${code}, сумма к погашению: ${formatAmount(target)} ${code}`;
    return isZeroAmount(difference) ? base : `${base}. Расхождение: ${formatAmount(difference)} ${code}`;
  }

  submitGenerate(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.generateForm.invalid) {
      this.generateForm.markAllAsTouched();
      return;
    }
    const value = this.generateForm.getRawValue();
    this.generate.emit({ firstPaymentDate: value.firstPaymentDate, numberOfPayments: Number(value.numberOfPayments) });
  }

  submitReplace(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.rows.invalid) {
      this.rows.markAllAsTouched();
      return;
    }
    this.replace.emit(this.rows.getRawValue().map((row) => ({ dueDate: row.dueDate, amount: row.amount.trim() })));
  }

  countError(): string | null {
    return fieldError(this.generateForm.controls.numberOfPayments, 'Целое число от 1 до 600');
  }

  firstDateError(): string | null {
    return fieldError(this.generateForm.controls.firstPaymentDate, 'Укажите дату первого платежа');
  }

  rowAmountError(row: ScheduleRow): string | null {
    return fieldError(row.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Число с точкой (1234.56)',
      positiveAmount: 'Больше нуля',
    });
  }

  rowDateError(row: ScheduleRow): string | null {
    return fieldError(row.controls.dueDate, 'Укажите дату');
  }

  private createRow(dueDate: string, amount: string): ScheduleRow {
    return this.formBuilder.nonNullable.group({
      dueDate: [dueDate, Validators.required],
      amount: [amount, [Validators.required, amountValidator, positiveAmountValidator]],
    });
  }
}
