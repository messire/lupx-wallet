import { DestroyRef, ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { CreateTransferRequest } from '../../../../data-access/transfers/transfers-api.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

export interface TransferWalletOption {
  id: string;
  name: string;
  currencyId: string;
}

/** Source and target wallet cannot be the same (UC-16). */
const differentWalletsValidator: ValidatorFn = (control): ValidationErrors | null => {
  const sourceWalletId = control.get('sourceWalletId')?.value;
  const targetWalletId = control.get('targetWalletId')?.value;
  if (sourceWalletId && targetWalletId && sourceWalletId === targetWalletId) {
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
  imports: [ReactiveFormsModule, ButtonComponent, CardComponent, FieldComponent, FieldControlDirective],
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

  /** Target wallets: same currency as the source, excluding the source itself (UC-16/UC-17). */
  readonly targetWalletOptions = computed<TransferWalletOption[]>(() => {
    const source = this.wallets().find((wallet) => wallet.id === this.sourceWalletId());
    if (!source) {
      return [];
    }
    return this.wallets().filter((wallet) => wallet.id !== source.id && wallet.currencyId === source.currencyId);
  });

  readonly form = this.formBuilder.nonNullable.group(
    {
      sourceWalletId: ['', Validators.required],
      targetWalletId: ['', Validators.required],
      amount: ['', [Validators.required, amountValidator]],
      transferDate: [this.today(), Validators.required],
    },
    { validators: differentWalletsValidator },
  );

  constructor() {
    this.form.controls.sourceWalletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => {
      this.sourceWalletId.set(id);
      // Changing the source wallet may invalidate the current target selection (different currency).
      if (this.form.controls.targetWalletId.value) {
        this.form.controls.targetWalletId.setValue('');
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

  amountError(): string | null {
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
    });
  }

  transferDateError(): string | null {
    return fieldError(this.form.controls.transferDate, 'Укажите дату');
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
