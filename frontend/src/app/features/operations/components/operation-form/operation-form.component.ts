import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, computed, effect, inject, input, output, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { amountValidator } from '../../../../shared/money/money';
import { AdjustmentMode, CreateOperationRequest, Operation, UpdateOperationRequest } from '../../../../data-access/operations/operations-api.models';
import { OperationTypeOption } from '../../operations.models';

export interface WalletOption {
  id: string;
  name: string;
}

/**
 * Форма создания/редактирования операции (UC-11 доход, UC-12 расход, UC-13
 * редактирование, UC-24 корректировка). Режим корректировки (Absolute/Delta,
 * Q3) показывается только когда выбранный тип операции имеет
 * behaviorKind = Adjustment.
 *
 * Поле кошелька доступно и в режиме редактирования (UC-13, решение пользователя от
 * 2026-09-11) — смена кошелька операции переносит ее на другой кошелек с пересчетом
 * баланса обоих (OperationUpdateRequest.walletId, openapi.yaml).
 */
@Component({
  selector: 'app-operation-form',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './operation-form.component.html',
  styleUrl: './operation-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationFormComponent {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly wallets = input.required<WalletOption[]>();
  /** Только активные типы, допустимые для прямого создания через /operations (без behaviorKind = Transfer). */
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

  private updateSelectedBehaviorKind(typeId: string | null): void {
    const type = this.operationTypes().find((candidate) => candidate.id === typeId);
    this.selectedBehaviorKindCode.set(type?.behaviorKindCode ?? null);
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
