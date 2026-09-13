import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { finalize } from 'rxjs';
import { FormBuilder, FormControl, FormsModule, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { amountValidator, formatMoney } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { ISO_CURRENCIES } from '@shared/currencies/iso-currencies';
import { CurrencySelectComponent } from '@shared/currencies/currency-select/currency-select.component';
import { Currency, ReferenceItem } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { UpdateWalletRequest, Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { CheckboxComponent } from '@shared/ui/checkbox/checkbox.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

/** An action awaiting user confirmation before an irreversible API call. */
interface PendingWalletAction {
  walletId: string;
  action: 'archive' | 'delete';
}

/**
 * UC-01 (create), UC-02…UC-06 (edit, archive, set primary, change currency,
 * delete) and UC-07 (list) — docs/requirements/user-scenarios.md. walletTypeId/
 * currencyId are chosen from the ReferenceData module's reference lists; next to
 * each list there is a quick-add mini-form, reused by the reference-data
 * management screen (features/reference-data).
 */
@Component({
  selector: 'app-wallets',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    FormsModule,
    RouterLink,
    CurrencySelectComponent,
    ButtonComponent,
    FieldComponent,
    FieldControlDirective,
    CheckboxComponent,
    BadgeComponent,
    CardComponent,
    DialogComponent,
    EmptyStateComponent,
    LoadingStateComponent,
    ErrorStateComponent,
    SkeletonComponent,
    ToastComponent,
  ],
  templateUrl: './wallets-page.component.html',
  styleUrl: './wallets-page.component.scss',
})
export class WalletsComponent implements OnInit {
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  /** shared/money/money.ts — formats Money for the table without casting to number. */
  protected readonly formatMoney = formatMoney;

  readonly wallets = signal<Wallet[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  /** UC-07: the list hides archived wallets by default (WalletsApiService.list). */
  readonly showArchived = signal(false);

  readonly walletTypes = signal<ReferenceItem[]>([]);
  readonly walletTypesLoading = signal(false);
  readonly walletTypesLoadError = signal<string | null>(null);

  readonly currencies = signal<Currency[]>([]);
  readonly currenciesLoading = signal(false);
  readonly currenciesLoadError = signal<string | null>(null);

  readonly isFormOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);

  /** Brief success confirmation (ui-kit §5 "Empty, loading, error, feedback") — app-toast, auto-dismisses. */
  readonly successMessage = signal<string | null>(null);

  readonly isAddingWalletType = signal(false);
  readonly newWalletTypeName = signal('');
  readonly isSubmittingWalletType = signal(false);
  readonly addWalletTypeError = signal<string | null>(null);

  readonly isAddingCurrency = signal(false);
  readonly newCurrencyIsoCode = new FormControl<string | null>(null);
  readonly isSubmittingCurrency = signal(false);
  readonly addCurrencyError = signal<string | null>(null);

  /** ISO_CURRENCIES minus codes already present in the reference list — avoid offering duplicates. */
  readonly availableIsoCurrencies = computed(() => {
    const existingCodes = new Set(this.currencies().map((currency) => currency.code));
    return ISO_CURRENCIES.filter((currency) => !existingCodes.has(currency.code));
  });

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    walletTypeId: ['', Validators.required],
    currencyId: ['', Validators.required],
    initialBalanceAmount: ['0', [Validators.required, amountValidator]],
    accountingStartDate: [this.today(), Validators.required],
  });

  /** UC-02 — PATCH /wallets/{id}, no currency field (see currencyForm below for UC-06). */
  readonly editingWallet = signal<Wallet | null>(null);
  readonly isEditSubmitting = signal(false);
  readonly editError = signal<string | null>(null);

  readonly editForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    walletTypeId: ['', Validators.required],
    purposeDescription: [''],
    includeInTotal: [true],
    displayOrder: [0],
    color: [''],
    icon: [''],
  });

  /** UC-06 — PUT /wallets/{id}/currency, a separate small form. */
  readonly changingCurrencyWalletId = signal<string | null>(null);
  readonly isCurrencySubmitting = signal(false);
  readonly currencyError = signal<string | null>(null);

  readonly currencyForm = this.formBuilder.nonNullable.group({
    currencyId: ['', Validators.required],
  });

  /** UC-03/UC-04 — archiving and deleting are irreversible, require confirmation. */
  readonly pendingConfirm = signal<PendingWalletAction | null>(null);
  /** UC-03…UC-05: shared error message for archive/set-primary/delete. */
  readonly actionError = signal<string | null>(null);

  ngOnInit(): void {
    this.loadWallets();
    this.loadWalletTypes();
    this.loadCurrencies();
  }

  loadWallets(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.walletsService.list({ includeArchived: this.showArchived() }).subscribe({
      next: (page) => {
        this.wallets.set(page.data);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить список кошельков.'));
        this.isLoading.set(false);
      },
    });
  }

  toggleShowArchived(): void {
    this.showArchived.set(!this.showArchived());
    this.loadWallets();
  }

  loadWalletTypes(): void {
    this.walletTypesLoading.set(true);
    this.walletTypesLoadError.set(null);

    this.referenceDataService
      .listWalletTypes({ limit: 100 })
      .pipe(finalize(() => this.walletTypesLoading.set(false)))
      .subscribe({
        next: (page) => this.walletTypes.set(page.data),
        error: (error: unknown) => this.walletTypesLoadError.set(extractErrorMessage(error, 'Не удалось загрузить типы кошельков.')),
      });
  }

  loadCurrencies(): void {
    this.currenciesLoading.set(true);
    this.currenciesLoadError.set(null);

    this.referenceDataService
      .listCurrencies({ limit: 100 })
      .pipe(finalize(() => this.currenciesLoading.set(false)))
      .subscribe({
        next: (page) => this.currencies.set(page.data),
        error: (error: unknown) => this.currenciesLoadError.set(extractErrorMessage(error, 'Не удалось загрузить валюты.')),
      });
  }

  openForm(): void {
    this.submitError.set(null);
    this.isFormOpen.set(true);
  }

  cancelForm(): void {
    this.isFormOpen.set(false);
    this.form.reset({ name: '', walletTypeId: '', currencyId: '', initialBalanceAmount: '0', accountingStartDate: this.today() });
  }

  submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    this.submitError.set(null);
    this.isSubmitting.set(true);

    this.walletsService.create(this.form.getRawValue()).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.cancelForm();
        this.loadWallets();
        this.successMessage.set('Кошелек создан');
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(extractErrorMessage(error, 'Не удалось создать кошелек.'));
      },
    });
  }

  toggleAddWalletType(): void {
    this.isAddingWalletType.set(!this.isAddingWalletType());
    this.newWalletTypeName.set('');
    this.addWalletTypeError.set(null);
  }

  submitNewWalletType(): void {
    const name = this.newWalletTypeName().trim();
    if (!name || this.isSubmittingWalletType()) {
      return;
    }

    this.isSubmittingWalletType.set(true);
    this.addWalletTypeError.set(null);

    this.referenceDataService
      .createWalletType(name)
      .pipe(finalize(() => this.isSubmittingWalletType.set(false)))
      .subscribe({
        next: (created) => {
          this.loadWalletTypes();
          this.form.patchValue({ walletTypeId: created.id });
          this.isAddingWalletType.set(false);
          this.newWalletTypeName.set('');
        },
        // Input is deliberately not cleared — the user can adjust and resubmit.
        error: (error: unknown) => this.addWalletTypeError.set(extractErrorMessage(error, 'Не удалось добавить тип кошелька.')),
      });
  }

  toggleAddCurrency(): void {
    this.isAddingCurrency.set(!this.isAddingCurrency());
    this.newCurrencyIsoCode.reset(null);
    this.addCurrencyError.set(null);
  }

  submitNewCurrency(): void {
    const isoCurrency = ISO_CURRENCIES.find((currency) => currency.code === this.newCurrencyIsoCode.value);
    if (!isoCurrency || this.isSubmittingCurrency()) {
      return;
    }

    this.isSubmittingCurrency.set(true);
    this.addCurrencyError.set(null);

    this.referenceDataService
      .createCurrency(isoCurrency.code, isoCurrency.name)
      .pipe(finalize(() => this.isSubmittingCurrency.set(false)))
      .subscribe({
        next: (created) => {
          this.loadCurrencies();
          this.form.patchValue({ currencyId: created.id });
          this.isAddingCurrency.set(false);
          this.newCurrencyIsoCode.reset(null);
        },
        // The selected currency is deliberately not reset — the user can resubmit.
        error: (error: unknown) => this.addCurrencyError.set(extractErrorMessage(error, 'Не удалось добавить валюту.')),
      });
  }

  // ---- UC-02: edit wallet (PATCH, no currency) ----

  openEditForm(wallet: Wallet): void {
    this.actionError.set(null);
    this.editError.set(null);
    this.changingCurrencyWalletId.set(null);
    this.editingWallet.set(wallet);
    this.editForm.reset({
      name: wallet.name,
      walletTypeId: wallet.walletTypeId,
      purposeDescription: wallet.purposeDescription ?? '',
      includeInTotal: wallet.includeInTotal,
      displayOrder: wallet.displayOrder,
      color: wallet.color ?? '',
      icon: wallet.icon ?? '',
    });

    // Q8: includeInTotal cannot be turned off for the primary wallet — the backend
    // silently ignores it (not a 409), so the field is disabled in the form to avoid
    // giving the false impression that turning it off would be saved.
    if (wallet.isPrimary) {
      this.editForm.controls.includeInTotal.disable();
    } else {
      this.editForm.controls.includeInTotal.enable();
    }
  }

  cancelEdit(): void {
    this.editingWallet.set(null);
    this.editError.set(null);
  }

  submitEdit(): void {
    const wallet = this.editingWallet();
    if (!wallet || this.editForm.invalid || this.isEditSubmitting()) {
      return;
    }

    this.isEditSubmitting.set(true);
    this.editError.set(null);

    const raw = this.editForm.getRawValue();
    const request: UpdateWalletRequest = {
      name: raw.name,
      walletTypeId: raw.walletTypeId,
      purposeDescription: raw.purposeDescription || null,
      includeInTotal: raw.includeInTotal,
      displayOrder: raw.displayOrder,
      color: raw.color || null,
      icon: raw.icon || null,
    };

    this.walletsService.update(wallet.id, request).subscribe({
      next: () => {
        this.isEditSubmitting.set(false);
        this.editingWallet.set(null);
        this.loadWallets();
        this.successMessage.set('Кошелек сохранён');
      },
      error: (error: unknown) => {
        this.isEditSubmitting.set(false);
        this.editError.set(extractErrorMessage(error, 'Не удалось изменить кошелек.'));
      },
    });
  }

  // ---- UC-06: change currency (PUT /wallets/{id}/currency) ----

  openChangeCurrency(wallet: Wallet): void {
    this.actionError.set(null);
    this.currencyError.set(null);
    this.editingWallet.set(null);
    this.currencyForm.reset({ currencyId: wallet.currencyId });
    this.changingCurrencyWalletId.set(wallet.id);
  }

  cancelChangeCurrency(): void {
    this.changingCurrencyWalletId.set(null);
    this.currencyError.set(null);
  }

  submitChangeCurrency(): void {
    const walletId = this.changingCurrencyWalletId();
    if (!walletId || this.currencyForm.invalid || this.isCurrencySubmitting()) {
      return;
    }

    this.isCurrencySubmitting.set(true);
    this.currencyError.set(null);

    this.walletsService.changeCurrency(walletId, this.currencyForm.getRawValue()).subscribe({
      next: () => {
        this.isCurrencySubmitting.set(false);
        this.changingCurrencyWalletId.set(null);
        this.loadWallets();
        this.successMessage.set('Валюта изменена');
      },
      error: (error: unknown) => {
        this.isCurrencySubmitting.set(false);
        this.currencyError.set(
          extractErrorMessage(error, 'Не удалось сменить валюту: у кошелька уже есть операции или слепки баланса.'),
        );
      },
    });
  }

  // ---- UC-05: set as primary (POST /wallets/{id}/set-primary) ----

  setPrimary(wallet: Wallet): void {
    this.actionError.set(null);

    this.walletsService.setPrimary(wallet.id).subscribe({
      next: () => {
        this.loadWallets();
        this.successMessage.set('Кошелек назначен основным');
      },
      error: (error: unknown) => {
        this.actionError.set(
          extractErrorMessage(error, 'Не удалось назначить кошелек основным: архивный кошелек нельзя сделать основным.'),
        );
      },
    });
  }

  // ---- UC-03/UC-04: archive and delete — irreversible actions, require confirmation ----

  requestConfirm(walletId: string, action: 'archive' | 'delete'): void {
    this.actionError.set(null);
    this.pendingConfirm.set({ walletId, action });
  }

  cancelConfirm(): void {
    this.pendingConfirm.set(null);
  }

  confirmPendingAction(): void {
    const pending = this.pendingConfirm();
    if (!pending) {
      return;
    }
    this.pendingConfirm.set(null);

    if (pending.action === 'archive') {
      this.walletsService.archive(pending.walletId).subscribe({
        next: () => {
          this.loadWallets();
          this.successMessage.set('Кошелек архивирован');
        },
        error: (error: unknown) => {
          this.actionError.set(
            extractErrorMessage(
              error,
              'Не удалось архивировать кошелек: нельзя архивировать текущий основной кошелек — сначала назначьте другой основным.',
            ),
          );
        },
      });
    } else {
      this.walletsService.remove(pending.walletId).subscribe({
        next: () => {
          this.loadWallets();
          this.successMessage.set('Кошелек удалён');
        },
        error: (error: unknown) => {
          this.actionError.set(
            extractErrorMessage(
              error,
              'Не удалось удалить кошелек: возможно, он основной или уже имеет историю операций — используйте архивирование.',
            ),
          );
        },
      });
    }
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }

  // ---- Display (template): pure view helpers, no business rules or HTTP ----

  /** Currency code by id from the already loaded reference list — for explicit currency display next to a balance (ui-kit §5). */
  protected currencyCode(currencyId: string): string | undefined {
    return this.currencies().find((currency) => currency.id === currencyId)?.code;
  }

  /** Wallet by id — used to fill in the name/details in the archive/delete confirmation dialog. */
  protected walletById(walletId: string): Wallet | undefined {
    return this.wallets().find((wallet) => wallet.id === walletId);
  }

  /** "Selected" card (ui-kit §5 "Wallet card") — the edit or change-currency form for this wallet is open. */
  protected isWalletSelected(walletId: string): boolean {
    return this.editingWallet()?.id === walletId || this.changingCurrencyWalletId() === walletId;
  }

  protected initialBalanceError(): string | null {
    return fieldError(this.form.controls.initialBalanceAmount, {
      required: 'Укажите начальный баланс',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
    });
  }
}
