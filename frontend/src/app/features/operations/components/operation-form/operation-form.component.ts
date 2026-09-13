import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { amountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { AdjustmentMode, CreateOperationRequest, Operation, UpdateOperationRequest } from '../../../../data-access/operations/operations-api.models';
import { OperationTypeOption } from '../../operations.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { RadioGroupComponent } from '@shared/ui/radio/radio-group.component';
import { RadioOptionComponent } from '@shared/ui/radio/radio-option.component';

export interface WalletOption {
  id: string;
  name: string;
}

/**
 * Operation create/edit form (UC-11 income, UC-12 expense, UC-13 edit, UC-24
 * adjustment). The adjustment mode (Absolute/Delta, Q3) is shown only when the
 * selected operation type has behaviorKind = Adjustment.
 *
 * The wallet field is also available in edit mode (UC-13) — changing the
 * operation's wallet moves it to another wallet with both balances
 * recalculated (OperationUpdateRequest.walletId, openapi.yaml).
 */
@Component({
  selector: 'app-operation-form',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    ButtonComponent,
    CardComponent,
    FieldComponent,
    FieldControlDirective,
    RadioGroupComponent,
    RadioOptionComponent,
  ],
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

  readonly form = this.formBuilder.nonNullable.group({
    walletId: ['', Validators.required],
    operationTypeId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator]],
    operationDate: [this.today(), Validators.required],
    adjustmentMode: ['Delta' as AdjustmentMode],
  });

  constructor() {
    this.form.controls.operationTypeId.valueChanges
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((typeId) => this.updateSelectedBehaviorKind(typeId));

    effect(() => {
      const operation = this.editingOperation();
      if (operation) {
        this.form.patchValue({
          walletId: operation.walletId,
          operationTypeId: operation.operationTypeId,
          amount: operation.amount.amount,
          operationDate: operation.operationDate,
          adjustmentMode: operation.adjustmentMode ?? 'Delta',
        });
        this.updateSelectedBehaviorKind(operation.operationTypeId);
      }
    });
  }

  submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    const value = this.form.getRawValue();
    const adjustment = this.isAdjustment() ? { adjustmentMode: value.adjustmentMode } : {};

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

  private updateSelectedBehaviorKind(typeId: string | null): void {
    const type = this.operationTypes().find((candidate) => candidate.id === typeId);
    this.selectedBehaviorKindCode.set(type?.behaviorKindCode ?? null);
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
