import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { amountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { CreateOperationRequest, Operation, UpdateOperationRequest } from '../../../../data-access/operations/operations-api.models';
import { OperationTypeOption } from '../../operations.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

export interface WalletOption {
  id: string;
  name: string;
  currencyCode: string;
  /** Start of the wallet accounting (yyyy-MM-dd); the lower bound of a stated-balance date. */
  accountingStartDate?: string;
}

/**
 * Operation create/edit form (UC-11 income, UC-12 expense, UC-13 edit, UC-24
 * adjustment). An adjustment (selected operation type has behaviorKind = Adjustment)
 * is always a signed change of the balance (ADR-0003 §6): the amount is entered with
 * a sign and `adjustmentMode = Delta` is sent. A new balance value is stated with a
 * separate form (`StatedBalanceFormComponent`).
 *
 * The wallet field is also available in edit mode (UC-13) — changing the
 * operation's wallet moves it to another wallet with both balances
 * recalculated (OperationUpdateRequest.walletId, openapi.yaml).
 */
@Component({
  selector: 'app-operation-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, FieldComponent, FieldControlDirective],
  templateUrl: './operation-form.component.html',
  styleUrl: './operation-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly wallets = input.required<WalletOption[]>();
  /** Only active types allowed for direct creation via /operations (excludes behaviorKind = Transfer). */
  readonly operationTypes = input.required<OperationTypeOption[]>();
  readonly editingOperation = input<Operation | null>(null);
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<CreateOperationRequest | UpdateOperationRequest>();
  readonly cancel = output<void>();

  readonly isEditMode = computed(() => this.editingOperation() !== null);
  readonly selectedBehaviorKindCode = signal<string | null>(null);
  readonly isAdjustment = computed(() => this.selectedBehaviorKindCode() === 'Adjustment');

  /** ui-kit §5 "Поля, select, checkbox, radio": сумма и валюта показаны рядом. */
  readonly selectedWalletId = signal<string | null>(null);
  readonly selectedWalletCurrencyCode = computed(
    () => this.wallets().find((wallet) => wallet.id === this.selectedWalletId())?.currencyCode ?? '',
  );

  readonly form = this.formBuilder.nonNullable.group({
    walletId: ['', Validators.required],
    operationTypeId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator]],
    operationDate: [this.today(), Validators.required],
  });

  constructor() {
    this.form.controls.operationTypeId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((typeId) => this.updateSelectedBehaviorKind(typeId));

    this.form.controls.walletId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((walletId) => this.selectedWalletId.set(walletId || null));

    effect(() => {
      const operation = this.editingOperation();
      if (operation) {
        this.form.patchValue({
          walletId: operation.walletId,
          operationTypeId: operation.operationTypeId ?? '',
          amount: operation.amount.amount,
          operationDate: operation.operationDate,
        });
        this.updateSelectedBehaviorKind(operation.operationTypeId);
        this.selectedWalletId.set(operation.walletId);
      }
    });

    // screen-spec.md "Операции": ожидание блокирует поля, не только повторную
    // отправку (submit() guard above already blocks that part).
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    const value = this.form.getRawValue();
    const adjustment = this.isAdjustment() ? { adjustmentMode: 'Delta' as const } : {};

    if (this.isEditMode()) {
      const request: UpdateOperationRequest = {
        walletId: value.walletId,
        operationTypeId: value.operationTypeId,
        amount: value.amount,
        operationDate: value.operationDate,
        ...adjustment,
      };
      this.save.emit(request);
      return;
    }

    const request: CreateOperationRequest = {
      walletId: value.walletId,
      operationTypeId: value.operationTypeId,
      amount: value.amount,
      operationDate: value.operationDate,
      ...adjustment,
    };
    this.save.emit(request);
  }

  onCancel(): void {
    this.cancel.emit();
  }

  walletError(): string | null {
    return fieldError(this.form.controls.walletId, 'Выберите кошелек');
  }

  operationTypeError(): string | null {
    return fieldError(this.form.controls.operationTypeId, 'Выберите тип операции');
  }

  operationDateError(): string | null {
    return fieldError(this.form.controls.operationDate, 'Укажите дату');
  }

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
    });
  }

  /**
   * Hint under the amount of an adjustment (the label is "Изменение на сумму (+/−)"): the sign carries the direction
   * of the change, and the way to state the actual balance for a date is the separate "Указать баланс" action
   * (ADR-0003 §6, p.9; the hint is plain text, as a link would have to close this dialog and open the other one).
   */
  readonly amountHint = computed(() =>
    this.isAdjustment()
      ? 'Изменение на сумму: плюс увеличивает баланс, минус уменьшает. ' +
        'Чтобы задать фактический баланс на дату, используйте «Указать баланс»'
      : null,
  );

  private updateSelectedBehaviorKind(typeId: string | null): void {
    const type = this.operationTypes().find((candidate) => candidate.id === typeId);
    this.selectedBehaviorKindCode.set(type?.behaviorKindCode ?? null);
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
