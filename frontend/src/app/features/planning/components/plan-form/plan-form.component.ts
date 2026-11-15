import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { PlannedTransaction, PlannedTransactionWriteRequest, RecurrenceFrequency } from '../../../../data-access/planning/planning-api.models';
import { Currency, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { todayIsoDate } from '@shared/ui/date-input/calendar-date';
import { FREQUENCY_OPTIONS, PlanWalletOption } from '../../planning.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CheckboxComponent } from '@shared/ui/checkbox/checkbox.component';
import { DateInputComponent } from '@shared/ui/date-input/date-input.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { RadioGroupComponent } from '@shared/ui/radio/radio-group.component';
import { RadioOptionComponent } from '@shared/ui/radio/radio-option.component';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/** An operation type with its direction, resolved by the page from the behavior kinds. */
export interface PlanTypeOption {
  type: OperationType;
  direction: 'Income' | 'Expense';
}

type EndMode = 'never' | 'until' | 'count';

/**
 * Planned transaction create/edit form (#9, ADR-0010 §6): an income or expense operation type (the direction comes
 * from it), amount, currency, optional wallet in that currency, description, the first occurrence date and an optional
 * recurrence rule — frequency, interval and an end (never / until a date / after N occurrences). PATCH replaces every
 * field; once the plan has executions its currency and direction cannot change (the server answers 409).
 */
@Component({
  selector: 'app-plan-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    CheckboxComponent,
    DateInputComponent,
    FieldComponent,
    FieldControlDirective,
    RadioGroupComponent,
    RadioOptionComponent,
    SelectComponent,
    SelectOptionDirective,
  ],
  templateUrl: './plan-form.component.html',
  styleUrl: './plan-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly frequencies = FREQUENCY_OPTIONS;

  /** Income and expense types (inactive ones only for the edited plan's current type). */
  readonly types = input.required<PlanTypeOption[]>();
  readonly currencies = input.required<Currency[]>();
  readonly wallets = input.required<PlanWalletOption[]>();
  readonly plan = input<PlannedTransaction | null>(null);
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<PlannedTransactionWriteRequest>();
  readonly cancel = output<void>();

  private readonly currencyId = signal('');
  private readonly endMode = signal<EndMode>('never');
  readonly repeats = signal(false);

  readonly typeOptions = computed(() => {
    const currentTypeId = this.plan()?.operationTypeId;
    return this.types().filter((option) => option.type.isActive || option.type.id === currentTypeId);
  });

  readonly walletOptions = computed(() => this.wallets().filter((wallet) => wallet.currencyId === this.currencyId()));
  readonly currencyCode = computed(() => this.currencies().find((currency) => currency.id === this.currencyId())?.code ?? '');
  readonly showUntil = computed(() => this.repeats() && this.endMode() === 'until');
  readonly showCount = computed(() => this.repeats() && this.endMode() === 'count');

  private readonly untilAfterStart: ValidatorFn = (control): ValidationErrors | null => {
    const value = control.value as string;
    const start = this.form?.controls.startDate.value;
    return value && start && value < start ? { beforeStart: true } : null;
  };

  readonly form = this.formBuilder.nonNullable.group({
    operationTypeId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
    currencyId: ['', Validators.required],
    walletId: [''],
    description: ['', Validators.maxLength(500)],
    startDate: [todayIsoDate(), Validators.required],
    repeats: [false],
    frequency: ['Monthly' as RecurrenceFrequency],
    interval: [1, [Validators.required, Validators.min(1), Validators.max(1000), Validators.pattern(/^\d+$/)]],
    endMode: ['never' as EndMode],
    until: [''],
    count: [12, [Validators.required, Validators.min(1), Validators.pattern(/^\d+$/)]],
  });

  constructor() {
    this.form.controls.until.addValidators([Validators.required, this.untilAfterStart]);
    const controls = this.form.controls;
    controls.currencyId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      this.currencyId.set(id);
      // A wallet of another currency no longer fits the plan.
      if (controls.walletId.value && !this.wallets().some((wallet) => wallet.id === controls.walletId.value && wallet.currencyId === id)) {
        controls.walletId.setValue('');
      }
    });
    controls.repeats.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => this.repeats.set(value));
    controls.endMode.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((value) => this.endMode.set(value));
    controls.startDate.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() =>
      controls.until.updateValueAndValidity({ emitEvent: false }),
    );

    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
        return;
      }
      this.form.enable({ emitEvent: false });
      // Recurrence fields validate only when they are used.
      for (const [control, active] of [
        [controls.frequency, this.repeats()],
        [controls.interval, this.repeats()],
        [controls.endMode, this.repeats()],
        [controls.until, this.showUntil()],
        [controls.count, this.showCount()],
      ] as const) {
        if (!active) {
          control.disable({ emitEvent: false });
        }
      }
    });
  }

  ngOnInit(): void {
    const plan = this.plan();
    if (!plan) {
      if (this.currencies().length === 1) {
        this.form.controls.currencyId.setValue(this.currencies()[0].id);
      }
      return;
    }
    const recurrence = plan.recurrence ?? null;
    this.form.setValue({
      operationTypeId: plan.operationTypeId,
      amount: plan.amount.amount,
      currencyId: plan.amount.currencyId,
      walletId: plan.walletId ?? '',
      description: plan.description ?? '',
      startDate: plan.startDate,
      repeats: recurrence !== null,
      frequency: recurrence?.frequency ?? 'Monthly',
      interval: recurrence?.interval ?? 1,
      endMode: recurrence?.until ? 'until' : recurrence?.count ? 'count' : 'never',
      until: recurrence?.until ?? '',
      count: recurrence?.count ?? 12,
    });
  }

  directionLabel(option: PlanTypeOption): string {
    const suffix = option.direction === 'Income' ? 'доход' : 'расход';
    return option.type.isActive ? `${option.type.name} (${suffix})` : `${option.type.name} (${suffix}, деактивирован)`;
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
    this.save.emit({
      operationTypeId: value.operationTypeId,
      amount: value.amount.trim(),
      currencyId: value.currencyId,
      walletId: value.walletId || null,
      description: value.description.trim() || null,
      startDate: value.startDate,
      recurrence: value.repeats
        ? {
            frequency: value.frequency,
            interval: Number(value.interval),
            until: value.endMode === 'until' ? value.until : null,
            count: value.endMode === 'count' ? Number(value.count) : null,
          }
        : null,
    });
  }

  typeError(): string | null {
    return fieldError(this.form.controls.operationTypeId, 'Выберите тип операции');
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
    });
  }

  currencyError(): string | null {
    return fieldError(this.form.controls.currencyId, 'Выберите валюту');
  }

  intervalError(): string | null {
    return fieldError(this.form.controls.interval, 'Целое число от 1 до 1000');
  }

  untilError(): string | null {
    return fieldError(this.form.controls.until, { required: 'Укажите дату окончания', beforeStart: 'Не раньше даты первого наступления' });
  }

  countError(): string | null {
    return fieldError(this.form.controls.count, 'Целое число от 1');
  }

  walletHint(): string {
    return this.currencyId() && this.walletOptions().length === 0
      ? 'Нет кошельков в этой валюте — кошелёк можно выбрать при исполнении'
      : 'Необязательно. Подставится при исполнении';
  }
}
