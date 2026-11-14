import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, effect, inject, input, output, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn, Validators } from '@angular/forms';
import { amountValidator, compareAmounts, formatAmount, isValidAmount, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { FundAllocationRequest } from '../../../../data-access/funds/funds-api.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/** A wallet for a reserve/release: `available` is its free funds (reserve) or this fund's allocation (release). */
export interface AllocationWalletOption {
  id: string;
  name: string;
  available: string;
}

/**
 * Reserve money of a wallet in a fund, or release a reserve (#7, ADR-0010 §4, В7). The wallet's balance does not
 * change. A reserve may not exceed the wallet's free funds (balance − reserves of all active funds), a release — the
 * fund's allocation on that wallet; both are shown next to the wallet and checked here, the server re-checks (409).
 */
@Component({
  selector: 'app-allocation-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, FieldComponent, FieldControlDirective, SelectComponent, SelectOptionDirective],
  templateUrl: './allocation-form.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AllocationFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  readonly mode = input<'reserve' | 'release'>('reserve');
  readonly wallets = input.required<AllocationWalletOption[]>();
  readonly currencyCode = input('');
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<FundAllocationRequest>();
  readonly cancel = output<void>();

  private readonly walletId = signal('');
  readonly selectedWallet = computed(() => this.wallets().find((wallet) => wallet.id === this.walletId()) ?? null);

  private readonly notMoreThanAvailable: ValidatorFn = (control): ValidationErrors | null => {
    const value = String(control.value ?? '').trim();
    const wallet = this.selectedWallet();
    if (!wallet || !value || !isValidAmount(value)) {
      return null;
    }
    return compareAmounts(value, wallet.available) > 0 ? { moreThanAvailable: true } : null;
  };

  readonly form = this.formBuilder.nonNullable.group({
    walletId: ['', Validators.required],
    amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
  });

  constructor() {
    this.form.controls.amount.addValidators(this.notMoreThanAvailable);
    this.form.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((id) => this.walletId.set(id));
    effect(() => {
      this.selectedWallet();
      this.form.controls.amount.updateValueAndValidity({ emitEvent: false });
    });
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
    });
  }

  ngOnInit(): void {
    const wallets = this.wallets();
    if (wallets.length === 1) {
      this.form.controls.walletId.setValue(wallets[0].id);
    }
  }

  /** "Всё доступное" — fills the whole free amount / allocation. */
  fillAvailable(): void {
    const wallet = this.selectedWallet();
    if (wallet) {
      this.form.controls.amount.setValue(wallet.available);
      this.form.controls.amount.markAsTouched();
    }
  }

  availableHint(): string | null {
    const wallet = this.selectedWallet();
    if (!wallet) {
      return this.wallets().length === 0 ? this.emptyText() : null;
    }
    const prefix = this.mode() === 'reserve' ? 'Свободно в кошельке' : 'Зарезервировано в этом кошельке';
    return `${prefix}: ${formatAmount(wallet.available)} ${this.currencyCode()}`;
  }

  emptyText(): string {
    return this.mode() === 'reserve' ? `Нет кошельков в валюте ${this.currencyCode()}` : 'В фонде нет резервов';
  }

  canFillAvailable(): boolean {
    const wallet = this.selectedWallet();
    return !!wallet && compareAmounts(wallet.available, '0') > 0;
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
    this.save.emit({ walletId: value.walletId, amount: value.amount.trim() });
  }

  walletError(): string | null {
    return fieldError(this.form.controls.walletId, 'Выберите кошелёк');
  }

  amountError(): string | null {
    const wallet = this.selectedWallet();
    return fieldError(this.form.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
      moreThanAvailable:
        this.mode() === 'reserve'
          ? `Не больше свободных средств кошелька (${formatAmount(wallet?.available ?? '0')} ${this.currencyCode()})`
          : `Не больше резерва в этом кошельке (${formatAmount(wallet?.available ?? '0')} ${this.currencyCode()})`,
    });
  }
}
