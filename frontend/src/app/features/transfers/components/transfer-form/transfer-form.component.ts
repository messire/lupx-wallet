import { DestroyRef, ChangeDetectionStrategy, Component, computed, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator } from '../../../../shared/money/money';
import { CreateTransferRequest } from '../../../../data-access/transfers/transfers-api.models';

export interface TransferWalletOption {
  id: string;
  name: string;
  currencyId: string;
}

/** Исходный и целевой кошелек не могут совпадать (UC-16). */
const differentWalletsValidator: ValidatorFn = (control): ValidationErrors | null => {
  const sourceWalletId = control.get('sourceWalletId')?.value;
  const targetWalletId = control.get('targetWalletId')?.value;
  if (sourceWalletId && targetWalletId && sourceWalletId === targetWalletId) {
    return { sameWallet: true };
  }
  return null;
};

/**
 * Форма создания перевода (UC-16): исходный и целевой кошельки обязаны иметь
 * одинаковую валюту (UC-17 — иначе перевод запрещен правилами раздела 4).
 * Список целевых кошельков фильтруется на клиенте по валюте выбранного
 * исходного кошелька — сервер все равно проверяет и вернет 409, если
 * ограничение будет обойдено (openapi.yaml, POST /transfers).
 *
 * Только создание — редактирование перевода контрактом не предусмотрено
 * (openapi.yaml не содержит PATCH /transfers/{id}), только удаление.
 */
@Component({
  selector: 'app-transfer-form',
  standalone: true,
  imports: [ReactiveFormsModule],
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

  /** Целевые кошельки: та же валюта, что у исходного, и не тот же кошелек (UC-16/UC-17). */
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
      // Смена исходного кошелька могла сделать текущий выбор цели невалидным (другая валюта).
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

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
