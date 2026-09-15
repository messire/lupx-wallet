import { Component, computed, effect, ElementRef, HostListener, inject, OnInit, signal, viewChild } from '@angular/core';
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
import { CreateWalletRequest, UpdateWalletRequest, Wallet } from '../../../../data-access/wallets/wallets-api.models';
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

/** The five commands and one navigation link collapsed under the card's "···" menu. */
type WalletMenuAction = 'edit' | 'primary' | 'archive' | 'currency' | 'delete';

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
    // Дополнительные настройки (docs/design/wallet-form.html «Дополнительные настройки»,
    // свёрнуты при создании) — уже поддержаны CreateWalletRequest, но раньше не были
    // доступны в форме создания (docs/design/screen-spec.md «Полная форма кошелька»).
    purposeDescription: [''],
    includeInTotal: [true],
    displayOrder: [0],
    color: [''],
    icon: [''],
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

  /** Wallet card actions collapsed under "···" (ui-kit §5 "Карточка кошелька") — at
   *  most one open at a time, so a single id is enough. Rendered as one popover
   *  outside the card grid, not one per card — see the template for why. */
  readonly openMenuWalletId = signal<string | null>(null);
  readonly openMenuWallet = computed(() => {
    const id = this.openMenuWalletId();
    return id ? (this.wallets().find((wallet) => wallet.id === id) ?? null) : null;
  });
  /** Viewport-relative, computed from the trigger's own rect when opened (`position:
   *  fixed`) — matches the previous per-card `position: absolute; right: 0` anchor
   *  (same right edge as the trigger) without being contained by the card. Anchored
   *  from the bottom instead of the top when the trigger is close enough to the
   *  bottom of the viewport that opening downward would push part of the menu below
   *  the fold — with no way back to it afterwards, since scrolling closes the menu
   *  (see onWindowScrollForMenu). */
  readonly menuPopoverPosition = signal<{ top: number | null; bottom: number | null; right: number } | null>(null);
  /** Conservative estimate of the tallest possible popover (5 items + padding/gaps,
   *  see .wallet-card__menu-item/-popover) — used only to decide open-up vs
   *  open-down; a couple of px of slack here has no visible effect either way. */
  private static readonly MENU_POPOVER_MAX_HEIGHT = 260;

  /** First field of the edit/change-currency forms — focused once the form opens
   *  from a menu selection, since "Изменить"/"Сменить валюту" hand focus to the
   *  opened form rather than back to the "···" trigger. */
  private readonly editNameInput = viewChild<ElementRef<HTMLInputElement>>('editNameInput');
  private readonly currencyFormSelect = viewChild<ElementRef<HTMLSelectElement>>('currencyFormSelect');

  constructor() {
    effect(() => {
      if (this.editingWallet()) {
        this.editNameInput()?.nativeElement.focus();
      }
    });
    effect(() => {
      if (this.changingCurrencyWalletId()) {
        this.currencyFormSelect()?.nativeElement.focus();
      }
    });
  }

  ngOnInit(): void {
    this.loadWallets();
    this.loadWalletTypes();
    this.loadCurrencies();
  }

  loadWallets(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    // limit: 100 for parity with OperationsComponent.loadWallets() — the grid has no
    // "load more"/paging of its own yet (known gap, see docs/PROGRESS.md), this only
    // raises how many wallets fit on the one page it does show.
    this.walletsService.list({ includeArchived: this.showArchived(), limit: 100 }).subscribe({
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
    this.form.enable({ emitEvent: false });
    this.form.reset({
      name: '',
      walletTypeId: '',
      currencyId: '',
      initialBalanceAmount: '0',
      accountingStartDate: this.today(),
      purposeDescription: '',
      includeInTotal: true,
      displayOrder: 0,
      color: '',
      icon: '',
    });
  }

  submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    this.submitError.set(null);
    this.isSubmitting.set(true);
    // screen-spec.md "Полная форма кошелька": ожидание блокирует поля, не только повторную отправку.
    this.form.disable({ emitEvent: false });

    const raw = this.form.getRawValue();
    const request: CreateWalletRequest = {
      name: raw.name,
      walletTypeId: raw.walletTypeId,
      currencyId: raw.currencyId,
      initialBalanceAmount: raw.initialBalanceAmount,
      accountingStartDate: raw.accountingStartDate,
      includeInTotal: raw.includeInTotal,
      // Native number input emits null (not NaN) when cleared — the control's declared
      // type (number) lies about this without the coalesce.
      displayOrder: raw.displayOrder ?? 0,
      ...(raw.purposeDescription && { purposeDescription: raw.purposeDescription }),
      ...(raw.color && { color: raw.color }),
      ...(raw.icon && { icon: raw.icon }),
    };

    this.walletsService.create(request).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.cancelForm();
        this.loadWallets();
        this.successMessage.set('Кошелек создан');
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.form.enable({ emitEvent: false });
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
    this.newCurrencyIsoCode.disable({ emitEvent: false });

    this.referenceDataService
      .createCurrency(isoCurrency.code, isoCurrency.name)
      .pipe(finalize(() => {
        this.isSubmittingCurrency.set(false);
        this.newCurrencyIsoCode.enable({ emitEvent: false });
      }))
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
    this.editForm.enable({ emitEvent: false });
    this.editForm.reset({
      name: wallet.name,
      walletTypeId: wallet.walletTypeId,
      purposeDescription: wallet.purposeDescription ?? '',
      includeInTotal: wallet.includeInTotal,
      displayOrder: wallet.displayOrder,
      color: wallet.color ?? '',
      icon: wallet.icon ?? '',
    });
    this.applyIncludeInTotalLock(wallet);
  }

  /**
   * Q8: includeInTotal cannot be turned off for the primary wallet — the backend
   * silently ignores it (not a 409), so the field is disabled in the form to avoid
   * giving the false impression that turning it off would be saved.
   */
  private applyIncludeInTotalLock(wallet: Wallet): void {
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
    this.editForm.disable({ emitEvent: false });

    const raw = this.editForm.getRawValue();
    const request: UpdateWalletRequest = {
      name: raw.name,
      walletTypeId: raw.walletTypeId,
      purposeDescription: raw.purposeDescription || null,
      includeInTotal: raw.includeInTotal,
      displayOrder: raw.displayOrder ?? 0,
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
        this.editForm.enable({ emitEvent: false });
        this.applyIncludeInTotalLock(wallet);
        this.editError.set(extractErrorMessage(error, 'Не удалось изменить кошелек.'));
      },
    });
  }

  // ---- UC-06: change currency (PUT /wallets/{id}/currency) ----

  openChangeCurrency(wallet: Wallet): void {
    this.actionError.set(null);
    this.currencyError.set(null);
    this.editingWallet.set(null);
    this.currencyForm.enable({ emitEvent: false });
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
    this.currencyForm.disable({ emitEvent: false });

    this.walletsService.changeCurrency(walletId, this.currencyForm.getRawValue()).subscribe({
      next: () => {
        this.isCurrencySubmitting.set(false);
        this.changingCurrencyWalletId.set(null);
        this.loadWallets();
        this.successMessage.set('Валюта изменена');
      },
      error: (error: unknown) => {
        this.isCurrencySubmitting.set(false);
        this.currencyForm.enable({ emitEvent: false });
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

  // ---- Wallet card "···" menu ----

  toggleWalletMenu(walletId: string): void {
    if (this.openMenuWalletId() === walletId) {
      this.openMenuWalletId.set(null);
      this.menuPopoverPosition.set(null);
      return;
    }

    const trigger = document.querySelector<HTMLElement>(`[data-wallet-menu-trigger="${walletId}"]`);
    if (trigger) {
      const rect = trigger.getBoundingClientRect();
      const spaceBelow = window.innerHeight - rect.bottom;
      const openUpward = spaceBelow < WalletsComponent.MENU_POPOVER_MAX_HEIGHT && rect.top > spaceBelow;
      this.menuPopoverPosition.set(
        openUpward
          ? { top: null, bottom: window.innerHeight - rect.top + 4, right: window.innerWidth - rect.right }
          : { top: rect.bottom + 4, bottom: null, right: window.innerWidth - rect.right },
      );
    }
    this.openMenuWalletId.set(walletId);
  }

  /**
   * Runs one of the card's actions and closes the menu. "Изменить"/"Сменить валюту"
   * hand focus to the form they open (see the two `effect()`s in the constructor)
   * instead of the trigger button — closing the menu without refocusing lets that
   * happen; refocusing here first would move focus to the trigger for an instant
   * and then immediately away from it again, which is a distinction the task's
   * exception clause draws for a reason (a screen reader would announce the
   * trigger as focused before announcing the form). "Сделать основным" has no
   * follow-up surface, so it returns focus to the trigger directly. "Архивировать"/
   * "Удалить" open the existing confirm dialog — focus is returned to the trigger
   * first so that dialog captures the trigger (not a document.activeElement left
   * behind by a click on a button this menu is about to remove) as the element to
   * restore focus to once *it* closes.
   */
  selectWalletMenuAction(wallet: Wallet, action: WalletMenuAction): void {
    if (action === 'edit') {
      this.openMenuWalletId.set(null);
      this.openEditForm(wallet);
      return;
    }
    if (action === 'currency') {
      this.openMenuWalletId.set(null);
      this.openChangeCurrency(wallet);
      return;
    }

    this.closeWalletMenu(wallet.id);
    if (action === 'primary') {
      this.setPrimary(wallet);
    } else {
      this.requestConfirm(wallet.id, action);
    }
  }

  /** Closes the menu and returns focus to its trigger — the default per the task's
   *  spec ("после закрытия фокус возвращается на кнопку"), used by Escape, an
   *  outside click, and the two menu actions that don't hand off to a form. */
  private closeWalletMenu(walletId?: string): void {
    const idToRefocus = walletId ?? this.openMenuWalletId();
    this.openMenuWalletId.set(null);
    this.menuPopoverPosition.set(null);
    if (idToRefocus) {
      document.querySelector<HTMLElement>(`[data-wallet-menu-trigger="${idToRefocus}"]`)?.focus();
    }
  }

  @HostListener('document:keydown.escape')
  onDocumentEscapeForMenu(): void {
    if (this.openMenuWalletId()) {
      this.closeWalletMenu();
    }
  }

  @HostListener('document:click', ['$event'])
  onDocumentClickForMenu(event: MouseEvent): void {
    const openId = this.openMenuWalletId();
    if (!openId) {
      return;
    }
    const target = event.target as HTMLElement | null;
    const insideTrigger = target?.closest(`[data-wallet-menu-trigger="${openId}"]`);
    const insidePopover = target?.closest(`[data-wallet-menu-popover="${openId}"]`);
    if (!insideTrigger && !insidePopover) {
      // A plain dismissal, not a cancel gesture — don't fight whatever the user
      // just clicked by yanking focus back to the trigger.
      this.openMenuWalletId.set(null);
      this.menuPopoverPosition.set(null);
    }
  }

  /** The popover's position is computed once, on open (`toggleWalletMenu`) — it
   *  does not track scrolling. Closing on scroll avoids showing it drifted away
   *  from its trigger; a plain dismissal, so no refocus (same reasoning as the
   *  outside-click case above). */
  @HostListener('window:scroll')
  onWindowScrollForMenu(): void {
    if (this.openMenuWalletId()) {
      this.openMenuWalletId.set(null);
      this.menuPopoverPosition.set(null);
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
