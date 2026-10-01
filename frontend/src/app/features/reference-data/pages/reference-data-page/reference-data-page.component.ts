import { Component, computed, inject, OnInit, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { RouterLink } from '@angular/router';
import { finalize, forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { CurrencyOption } from '@shared/currencies/currency-option';
import { CurrencySelectComponent } from '@shared/currencies/currency-select/currency-select.component';
import { AvailableCurrency, Currency, OperationBehaviorKind, OperationType, ReferenceItem } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { TabDefinition, TabsComponent } from '@shared/ui/tabs/tabs.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

export type ReferenceTab = 'wallet-types' | 'operation-types' | 'currencies';

/**
 * UC-26 (wallet type list), UC-27 (operation type list), UC-28 (currencies) — a
 * single screen with three tabs. Wallet and operation types: list with an active
 * flag, create, deactivate, delete. Currencies work differently (ADR-0004 §7): the
 * list holds only active currencies, a currency is added to it from the list
 * provided by the exchange-rate service, and removed from it ("Убрать из активных")
 * — there is no deactivation. The list of available currencies is loaded as its own
 * stream with its own error state, so its failure never blocks the other tabs.
 *
 * HTTP calls go only through ReferenceDataApiService — the same service used by the
 * quick-add mini-forms on the wallets screen (features/wallets), so logic is not
 * duplicated.
 */
@Component({
  selector: 'app-reference-data',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CurrencySelectComponent,
    TabsComponent,
    ButtonComponent,
    FieldComponent,
    FieldControlDirective,
    BadgeComponent,
    EmptyStateComponent,
    LoadingStateComponent,
    SkeletonComponent,
    ErrorStateComponent,
    DialogComponent,
    ToastComponent,
  ],
  templateUrl: './reference-data-page.component.html',
  styleUrl: './reference-data-page.component.scss',
})
export class ReferenceDataComponent implements OnInit {
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  /** Panels switch in place (no route navigation) — app-tabs (ui-kit.md §5 "Tabs"). */
  readonly tabs: readonly TabDefinition[] = [
    { id: 'wallet-types', label: 'Типы кошельков' },
    { id: 'operation-types', label: 'Типы операций' },
    { id: 'currencies', label: 'Валюты' },
  ];

  readonly activeTab = signal<ReferenceTab>('wallet-types');

  readonly walletTypes = signal<ReferenceItem[]>([]);
  readonly operationTypes = signal<OperationType[]>([]);
  readonly currencies = signal<Currency[]>([]);
  readonly availableCurrencies = signal<AvailableCurrency[]>([]);
  readonly availableCurrenciesLoading = signal(false);
  readonly availableCurrenciesError = signal<string | null>(null);
  readonly behaviorKinds = signal<OperationBehaviorKind[]>([]);

  /**
   * Transfer is a system-only OperationBehaviorKind (created only as a pair of
   * transfer operations) — not offered to the user when creating a new operation type.
   */
  readonly creatableBehaviorKinds = computed(() => this.behaviorKinds().filter((kind) => kind.code !== 'Transfer'));

  /** Available currencies minus the ones already active — nothing to offer twice. */
  readonly addableCurrencies = computed<CurrencyOption[]>(() => {
    const activeCodes = new Set(this.currencies().map((currency) => currency.code));
    return this.availableCurrencies()
      .filter((currency) => !currency.isActive && !activeCodes.has(currency.code))
      .map(({ code, name }) => ({ code, name }));
  });

  /** Permanent hint under the "add" field: with nothing left to add the popup alone would be the only place to learn why. */
  protected readonly allCurrenciesAddedHint = 'Все доступные валюты уже добавлены';

  private readonly tabsComponent = viewChild(TabsComponent);

  /** The currency awaiting confirmation before it is removed from the active ones. */
  readonly pendingCurrencyRemoval = signal<Currency | null>(null);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  /** Brief success confirmation (ui-kit §5 "Empty, loading, error, feedback") — app-toast, auto-dismisses. */
  readonly successMessage = signal<string | null>(null);

  readonly walletTypeForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
  });
  readonly operationTypeForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    behaviorKindId: ['', Validators.required],
  });
  readonly currencyForm = this.formBuilder.nonNullable.group({
    code: ['', Validators.required],
  });

  readonly isSubmittingWalletType = signal(false);
  readonly isSubmittingOperationType = signal(false);
  readonly isSubmittingCurrency = signal(false);

  ngOnInit(): void {
    this.loadAll();
  }

  /** Accepts string — app-tabs' `selectedIdChange` is loosely typed; valid ids are constrained by `tabs` above. */
  selectTab(tab: string): void {
    this.activeTab.set(tab as ReferenceTab);
    this.actionError.set(null);
  }

  loadAll(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.loadAvailableCurrencies();

    // includeInactive: true for wallet and operation types — unlike the mini-forms on the
    // wallets screen (which only need active items for selection), this management screen
    // must also show deactivated items with their active flag. Currencies have no such state.
    forkJoin({
      walletTypes: this.referenceDataService.listWalletTypes({ includeInactive: true, limit: 100 }),
      operationTypes: this.referenceDataService.listOperationTypes({ includeInactive: true, limit: 100 }),
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      behaviorKinds: this.referenceDataService.listOperationBehaviorKinds(),
    }).subscribe({
      next: (result) => {
        this.walletTypes.set(result.walletTypes.data);
        this.operationTypes.set(result.operationTypes.data);
        this.currencies.set(result.currencies.data);
        this.behaviorKinds.set(result.behaviorKinds);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить справочники.'));
        this.isLoading.set(false);
      },
    });
  }

  /** Own stream with its own error: a failure here must not hide the wallet/operation type tabs. */
  loadAvailableCurrencies(): void {
    this.availableCurrenciesLoading.set(true);
    this.availableCurrenciesError.set(null);

    this.referenceDataService
      .listAvailableCurrencies()
      .pipe(finalize(() => this.availableCurrenciesLoading.set(false)))
      .subscribe({
        next: (currencies) => this.availableCurrencies.set(currencies),
        error: (error: unknown) =>
          this.availableCurrenciesError.set(extractErrorMessage(error, 'Не удалось загрузить список доступных валют.')),
      });
  }

  /** The retry button disappears with the error state, so focus moves to the (focusable) tab panel instead of falling back to the body. */
  retryAvailableCurrencies(): void {
    this.loadAvailableCurrencies();
    this.focusElement(this.tabsComponent()?.panelElementId('currencies'));
  }

  behaviorKindName(id: string): string {
    return this.behaviorKinds().find((kind) => kind.id === id)?.name ?? '—';
  }

  submitWalletType(): void {
    if (this.walletTypeForm.invalid || this.isSubmittingWalletType()) {
      return;
    }

    this.actionError.set(null);
    this.isSubmittingWalletType.set(true);

    this.referenceDataService.createWalletType(this.walletTypeForm.getRawValue().name).subscribe({
      next: () => {
        this.isSubmittingWalletType.set(false);
        this.walletTypeForm.reset({ name: '' });
        this.reloadWalletTypes();
      },
      error: (error: unknown) => {
        this.isSubmittingWalletType.set(false);
        this.actionError.set(extractErrorMessage(error, 'Не удалось создать тип кошелька.'));
      },
    });
  }

  deactivateWalletType(item: ReferenceItem): void {
    this.actionError.set(null);
    this.referenceDataService.deactivateWalletType(item.id).subscribe({
      next: () => this.reloadWalletTypes(),
      error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось деактивировать тип кошелька.')),
    });
  }

  deleteWalletType(item: ReferenceItem): void {
    this.actionError.set(null);
    this.referenceDataService.deleteWalletType(item.id).subscribe({
      next: () => this.reloadWalletTypes(),
      error: (error: unknown) =>
        this.actionError.set(extractErrorMessage(error, 'Не удалось удалить тип кошелька: возможно, он уже используется.')),
    });
  }

  submitOperationType(): void {
    if (this.operationTypeForm.invalid || this.isSubmittingOperationType()) {
      return;
    }

    this.actionError.set(null);
    this.isSubmittingOperationType.set(true);
    const { name, behaviorKindId } = this.operationTypeForm.getRawValue();

    this.referenceDataService.createOperationType(name, behaviorKindId).subscribe({
      next: () => {
        this.isSubmittingOperationType.set(false);
        this.operationTypeForm.reset({ name: '', behaviorKindId: '' });
        this.reloadOperationTypes();
      },
      error: (error: unknown) => {
        this.isSubmittingOperationType.set(false);
        this.actionError.set(extractErrorMessage(error, 'Не удалось создать тип операции.'));
      },
    });
  }

  deactivateOperationType(item: OperationType): void {
    this.actionError.set(null);
    this.referenceDataService.deactivateOperationType(item.id).subscribe({
      next: () => this.reloadOperationTypes(),
      error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось деактивировать тип операции.')),
    });
  }

  deleteOperationType(item: OperationType): void {
    this.actionError.set(null);
    this.referenceDataService.deleteOperationType(item.id).subscribe({
      next: () => this.reloadOperationTypes(),
      error: (error: unknown) =>
        this.actionError.set(extractErrorMessage(error, 'Не удалось удалить тип операции: возможно, он уже используется.')),
    });
  }

  submitCurrency(): void {
    if (this.currencyForm.invalid || this.isSubmittingCurrency()) {
      return;
    }

    this.actionError.set(null);
    this.isSubmittingCurrency.set(true);

    this.referenceDataService.createCurrency(this.currencyForm.getRawValue().code).subscribe({
      next: () => {
        this.isSubmittingCurrency.set(false);
        this.currencyForm.reset({ code: '' });
        this.successMessage.set('Валюта добавлена; курсы и история загружаются в фоне');
        this.reloadCurrencies();
      },
      error: (error: unknown) => {
        this.isSubmittingCurrency.set(false);
        this.actionError.set(this.currencyCreateError(error));
        if (error instanceof HttpErrorResponse && error.status === 409) {
          // Already active (added elsewhere): our lists are stale, and the chosen code must not stay selected invisibly.
          this.currencyForm.reset({ code: '' });
          this.reloadCurrencies();
        }
      },
    });
  }

  /** "Убрать из активных" is only offered after a confirmation: the currency's rates are removed with it. */
  requestCurrencyRemoval(item: Currency): void {
    this.actionError.set(null);
    this.pendingCurrencyRemoval.set(item);
  }

  cancelCurrencyRemoval(): void {
    this.pendingCurrencyRemoval.set(null);
  }

  confirmCurrencyRemoval(): void {
    const item = this.pendingCurrencyRemoval();
    if (!item) {
      return;
    }
    this.pendingCurrencyRemoval.set(null);

    this.referenceDataService.deleteCurrency(item.id).subscribe({
      // The clicked "Убрать из активных" button is removed together with its row, so focus goes to the tab.
      next: () => this.reloadCurrencies(true),
      error: (error: unknown) => this.actionError.set(this.currencyRemovalError(error)),
    });
  }

  private currencyCreateError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 400) {
      return extractErrorMessage(error, 'Валюта недоступна: её нет в списке сервиса курсов.');
    }
    if (error instanceof HttpErrorResponse && error.status === 409) {
      return extractErrorMessage(error, 'Валюта уже добавлена в активные.');
    }
    return extractErrorMessage(error, 'Не удалось добавить валюту.');
  }

  private currencyRemovalError(error: unknown): string {
    if (error instanceof HttpErrorResponse && error.status === 409) {
      return extractErrorMessage(error, 'Валюта используется кошельком или операцией (включая удалённые).');
    }
    return extractErrorMessage(error, 'Не удалось убрать валюту из активных.');
  }

  private reloadWalletTypes(): void {
    this.referenceDataService
      .listWalletTypes({ includeInactive: true, limit: 100 })
      .subscribe((page) => this.walletTypes.set(page.data));
  }

  private reloadOperationTypes(): void {
    this.referenceDataService
      .listOperationTypes({ includeInactive: true, limit: 100 })
      .subscribe((page) => this.operationTypes.set(page.data));
  }

  /** Both lists change together: an added/removed currency flips its `isActive` in the available list. Loaded as two independent streams. */
  private reloadCurrencies(restoreFocusToTab = false): void {
    this.loadAvailableCurrencies();
    this.referenceDataService
      .listCurrencies({ limit: 100 })
      .pipe(
        finalize(() => {
          if (restoreFocusToTab) {
            this.focusElement(this.tabsComponent()?.tabElementId('currencies'));
          }
        }),
      )
      .subscribe({
        next: (page) => this.currencies.set(page.data),
        error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось обновить список валют.')),
      });
  }

  private focusElement(id: string | undefined): void {
    if (id) {
      document.getElementById(id)?.focus();
    }
  }
}
