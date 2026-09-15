import { DestroyRef, ChangeDetectionStrategy, Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormGroup, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { CreateTransferRequest } from '../../../../data-access/transfers/transfers-api.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

export interface TransferWalletOption {
  id: string;
  name: string;
  currencyId: string;
  /** For the amount field's input-suffix (ui-kit §5) — resolved by the page from /currencies. */
  currencyCode: string;
  /** For the transferDate field's client-side lower bound (screen-spec.md "Переводы"). */
  accountingStartDate: string;
}

/**
 * screen-spec.md "Переводы" / user requirements (П.0.1): a transfer amount must be
 * positive. `amountValidator` alone only checks the string format (ADR-0005) and
 * deliberately allows a leading '-' for other forms (e.g. Operations' adjustment
 * "Delta" mode) — this validator adds the transfer-specific positivity rule without
 * touching the shared one. String-based (no `Number()`), per ADR-0005. A value made
 * up of only zeros/dot/whitespace (e.g. "0", "0.00") is rejected too — it parses as
 * a valid amount format but is not > 0, and the server rejects it
 * (OperationAmountMustBePositiveException).
 */
const positiveAmountValidator: ValidatorFn = (control): ValidationErrors | null => {
  const value = control.value;
  if (value === null || value === undefined || value === '') {
    return null;
  }
  const trimmed = String(value).trim();
  return trimmed.startsWith('-') || /^0*\.?0*$/.test(trimmed) ? { negativeAmount: true } : null;
};

/**
 * Source and target wallet cannot be the same (UC-16), and the target must be
 * chosen. Reads `getRawValue()`, not the group's own (enabled-only) `.value`:
 * `targetWalletId` is disabled when the source wallet has no currency-compatible
 * target (screen-spec.md "Переводы") — `FormGroup.invalid` ignores disabled
 * controls when aggregating status, so an empty, disabled target would otherwise
 * make the group appear valid and let a transfer submit with no recipient (MAJOR-1).
 */
const differentWalletsValidator: ValidatorFn = (control): ValidationErrors | null => {
  if (!(control instanceof FormGroup)) {
    return null;
  }
  const raw = control.getRawValue() as { sourceWalletId: string; targetWalletId: string };
  if (!raw.targetWalletId) {
    return { targetWalletRequired: true };
  }
  if (raw.sourceWalletId && raw.sourceWalletId === raw.targetWalletId) {
    return { sameWallet: true };
  }
  return null;
};

/**
 * Transfer create form (UC-16): the source and target wallet must share the
 * same currency (UC-17 — otherwise the transfer is forbidden by section 4's rules).
 * The target wallet list is filtered client-side by the selected source wallet's
 * currency — the server still validates it and returns 409 if the restriction
 * is bypassed (openapi.yaml, POST /transfers).
 *
 * Create only — editing a transfer is not part of the contract (openapi.yaml
 * has no PATCH /transfers/{id}), only deletion.
 */
@Component({
  selector: 'app-transfer-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, FieldComponent, FieldControlDirective],
  templateUrl: './transfer-form.component.html',
  styleUrl: './transfer-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransferFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly wallets = input.required<TransferWalletOption[]>();
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<CreateTransferRequest>();
  readonly cancel = output<void>();

  private readonly sourceWalletId = signal<string>('');
  private readonly targetWalletId = signal<string>('');

  readonly selectedSourceWallet = computed(() => this.wallets().find((wallet) => wallet.id === this.sourceWalletId()));

  /** Target wallets: same currency as the source, excluding the source itself (UC-16/UC-17). */
  readonly targetWalletOptions = computed<TransferWalletOption[]>(() => {
    const source = this.selectedSourceWallet();
    if (!source) {
      return [];
    }
    return this.wallets().filter((wallet) => wallet.id !== source.id && wallet.currencyId === source.currencyId);
  });

  /** ui-kit §5 "Поля, select, checkbox, radio": сумма и валюта показаны рядом — код валюты источника. */
  readonly selectedSourceCurrencyCode = computed(() => this.selectedSourceWallet()?.currencyCode ?? '');

  /**
   * Lower bound for `transferDate`: the later of the two selected wallets'
   * `accountingStartDate` (a transfer cannot be dated before either wallet
   * started being tracked). `null` while no wallet is selected yet. This is a
   * client-side convenience only — the server (`CreateTransferCommand`) is the
   * source of truth and its error stays visible over this client check.
   */
  readonly minTransferDate = computed<string | null>(() => {
    const dates = [this.selectedSourceWallet()?.accountingStartDate, this.wallets().find((wallet) => wallet.id === this.targetWalletId())?.accountingStartDate].filter(
      (date): date is string => !!date,
    );
    return dates.length === 0 ? null : dates.reduce((later, date) => (date > later ? date : later));
  });

  readonly maxTransferDate = this.today();

  private readonly transferDateRangeValidator: ValidatorFn = (control): ValidationErrors | null => {
    const value = control.value;
    if (!value) {
      return null;
    }
    const min = this.minTransferDate();
    if (min && value < min) {
      return { transferDateTooEarly: true };
    }
    if (value > this.maxTransferDate) {
      return { transferDateTooLate: true };
    }
    return null;
  };

  readonly form = this.formBuilder.nonNullable.group(
    {
      sourceWalletId: ['', Validators.required],
      targetWalletId: ['', Validators.required],
      amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
      transferDate: [this.today(), Validators.required],
    },
    { validators: differentWalletsValidator },
  );

  constructor() {
    // Attached here (not inline in the group() above) and revalidated only from the
    // effect below: the group() call runs during field initialization, before Angular
    // has set the required `wallets` input — validating eagerly at that point would
    // read it too early (NG0950). `addValidators` alone does not trigger validation.
    this.form.controls.transferDate.addValidators(this.transferDateRangeValidator);

    this.form.controls.sourceWalletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      this.sourceWalletId.set(id);
      // Changing the source wallet may invalidate the current target selection (different currency).
      if (this.form.controls.targetWalletId.value) {
        this.form.controls.targetWalletId.setValue('');
      }
    });

    this.form.controls.targetWalletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      this.targetWalletId.set(id);
    });

    // The date bound depends on wallet selection — re-run the validator whenever it moves.
    // This is also the first real validation pass for transferDate (see the comment above).
    effect(() => {
      this.minTransferDate();
      this.form.controls.transferDate.updateValueAndValidity({ emitEvent: false });
    });

    // screen-spec.md "Переводы": ожидание запроса блокирует поля, не только повторную
    // отправку (submit() guard below already blocks that part). The target wallet stays
    // disabled until a source is chosen ("Сначала выберите источник").
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
        return;
      }
      this.form.enable({ emitEvent: false });
      // screen-spec.md "Переводы": also disabled when a source is chosen but has no
      // currency-compatible target — the field would otherwise offer an empty dropdown.
      if (!this.sourceWalletId() || this.targetWalletOptions().length === 0) {
        this.form.controls.targetWalletId.disable({ emitEvent: false });
      }
    });
  }

  submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    const value = this.form.getRawValue();
    const request: CreateTransferRequest = {
      sourceWalletId: value.sourceWalletId,
      targetWalletId: value.targetWalletId,
      amount: value.amount,
      transferDate: value.transferDate,
    };
    this.save.emit(request);
  }

  onCancel(): void {
    this.cancel.emit();
  }

  sourceWalletError(): string | null {
    return fieldError(this.form.controls.sourceWalletId, 'Выберите кошелек-источник');
  }

  targetWalletError(): string | null {
    return fieldError(this.form.controls.targetWalletId, 'Выберите кошелек-получатель');
  }

  /** No techspeak (screen-spec.md "Переводы") — replaces the field's error/help depending on state. */
  targetWalletHint(): string | null {
    if (!this.sourceWalletId()) {
      return 'Сначала выберите источник';
    }
    if (this.targetWalletOptions().length === 0) {
      return 'Нет других кошельков с той же валютой — переводы между разными валютами не поддерживаются';
    }
    return null;
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      negativeAmount: 'Сумма перевода должна быть положительной',
    });
  }

  transferDateError(): string | null {
    return fieldError(this.form.controls.transferDate, {
      required: 'Укажите дату',
      transferDateTooEarly: `Дата не может быть раньше ${this.minTransferDate()} — начала учёта одного из кошельков`,
      transferDateTooLate: `Дата не может быть позже ${this.maxTransferDate}`,
    });
  }

  /** Shown below the date field even without an error, so the bound is visible up front. */
  transferDateHint(): string {
    const min = this.minTransferDate();
    return min ? `Дата — от ${min} до ${this.maxTransferDate}` : `Дата — не позже ${this.maxTransferDate}`;
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
