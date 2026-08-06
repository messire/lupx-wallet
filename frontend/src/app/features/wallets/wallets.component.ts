import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { extractErrorMessage } from '../../core/http/problem-details';
import { amountValidator, formatMoney } from '../../core/money/money';
import { Currency, ReferenceItem } from '../reference-data/reference-data.models';
import { ReferenceDataService } from '../reference-data/reference-data.service';
import { UpdateWalletRequest, Wallet } from './wallets.models';
import { WalletsService } from './wallets.service';

/** Действие, ожидающее подтверждения пользователем перед необратимым вызовом API. */
interface PendingWalletAction {
  walletId: string;
  action: 'archive' | 'delete';
}

/**
 * UC-01 (создание), UC-02…UC-06 (изменение, архивирование, назначение основным,
 * смена валюты, удаление) и UC-07 (список) — docs/requirements/user-scenarios.md.
 * walletTypeId/currencyId выбираются из справочников модуля ReferenceData; рядом с
 * каждым списком — мини-форма быстрого добавления элемента, так как отдельного
 * экрана управления справочниками пока нет (предмет отдельного среза UI).
 */
@Component({
  selector: 'app-wallets',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink],
  templateUrl: './wallets.component.html',
  styleUrl: './wallets.component.scss',
})
export class WalletsComponent implements OnInit {
  private readonly walletsService = inject(WalletsService);
  private readonly referenceDataService = inject(ReferenceDataService);
  private readonly formBuilder = inject(FormBuilder);

  /** core/money/money.ts — форматирование Money для таблицы, без приведения к number. */
  protected readonly formatMoney = formatMoney;

  readonly wallets = signal<Wallet[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  /** UC-07: список по умолчанию скрывает архивные кошельки (WalletsService.list). */
  readonly showArchived = signal(false);

  readonly walletTypes = signal<ReferenceItem[]>([]);
  readonly currencies = signal<Currency[]>([]);

  readonly isFormOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly isAddingWalletType = signal(false);
  readonly newWalletTypeName = signal('');
  readonly isAddingCurrency = signal(false);
  readonly newCurrencyCode = signal('');
  readonly newCurrencyName = signal('');

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    walletTypeId: ['', Validators.required],
    currencyId: ['', Validators.required],
    initialBalanceAmount: ['0', [Validators.required, amountValidator]],
    accountingStartDate: [this.today(), Validators.required],
  });

  /** UC-02 — PATCH /wallets/{id}, без валюты (см. currencyForm ниже для UC-06). */
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

  /** UC-06 — PUT /wallets/{id}/currency, отдельная маленькая форма. */
  readonly changingCurrencyWalletId = signal<string | null>(null);
  readonly isCurrencySubmitting = signal(false);
  readonly currencyError = signal<string | null>(null);

  readonly currencyForm = this.formBuilder.nonNullable.group({
    currencyId: ['', Validators.required],
  });

  /** UC-03/UC-04 — архивирование и удаление необратимы, требуют подтверждения. */
  readonly pendingConfirm = signal<PendingWalletAction | null>(null);
  /** UC-03…UC-05: общая строка ошибки для архивирования/назначения основным/удаления. */
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
    this.referenceDataService.listWalletTypes({ limit: 100 }).subscribe((page) => this.walletTypes.set(page.data));
  }

  loadCurrencies(): void {
    this.referenceDataService.listCurrencies({ limit: 100 }).subscribe((page) => this.currencies.set(page.data));
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
  }

  submitNewWalletType(): void {
    const name = this.newWalletTypeName().trim();
    if (!name) {
      return;
    }

    this.referenceDataService.createWalletType(name).subscribe((created) => {
      this.loadWalletTypes();
      this.form.patchValue({ walletTypeId: created.id });
      this.isAddingWalletType.set(false);
      this.newWalletTypeName.set('');
    });
  }

  toggleAddCurrency(): void {
    this.isAddingCurrency.set(!this.isAddingCurrency());
    this.newCurrencyCode.set('');
    this.newCurrencyName.set('');
  }

  submitNewCurrency(): void {
    const code = this.newCurrencyCode().trim();
    const name = this.newCurrencyName().trim();
    if (!code || !name) {
      return;
    }

    this.referenceDataService.createCurrency(code, name).subscribe((created) => {
      this.loadCurrencies();
      this.form.patchValue({ currencyId: created.id });
      this.isAddingCurrency.set(false);
      this.newCurrencyCode.set('');
      this.newCurrencyName.set('');
    });
  }

  // ---- UC-02: редактирование кошелька (PATCH, без валюты) ----

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

    // Q8: у основного кошелька includeInTotal нельзя выключить — бэкенд это молча
    // игнорирует (не 409), поле задизейблено в форме, чтобы не создавать ложное
    // впечатление, что выключение сохранится.
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
      },
      error: (error: unknown) => {
        this.isEditSubmitting.set(false);
        this.editError.set(extractErrorMessage(error, 'Не удалось изменить кошелек.'));
      },
    });
  }

  // ---- UC-06: смена валюты (PUT /wallets/{id}/currency) ----

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
      },
      error: (error: unknown) => {
        this.isCurrencySubmitting.set(false);
        this.currencyError.set(
          extractErrorMessage(error, 'Не удалось сменить валюту: у кошелька уже есть операции или слепки баланса.'),
        );
      },
    });
  }

  // ---- UC-05: назначение основным (POST /wallets/{id}/set-primary) ----

  setPrimary(wallet: Wallet): void {
    this.actionError.set(null);

    this.walletsService.setPrimary(wallet.id).subscribe({
      next: () => this.loadWallets(),
      error: (error: unknown) => {
        this.actionError.set(
          extractErrorMessage(error, 'Не удалось назначить кошелек основным: архивный кошелек нельзя сделать основным.'),
        );
      },
    });
  }

  // ---- UC-03/UC-04: архивирование и удаление — необратимые действия, с подтверждением ----

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
        next: () => this.loadWallets(),
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
        next: () => this.loadWallets(),
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
}
