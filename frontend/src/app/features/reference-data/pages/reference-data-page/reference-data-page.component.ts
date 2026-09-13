import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { ISO_CURRENCIES } from '@shared/currencies/iso-currencies';
import { CurrencySelectComponent } from '@shared/currencies/currency-select/currency-select.component';
import { Currency, OperationBehaviorKind, OperationType, ReferenceItem } from '../../../../data-access/reference-data/reference-data-api.models';
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

export type ReferenceTab = 'wallet-types' | 'operation-types' | 'currencies';

/**
 * UC-26 (wallet type list), UC-27 (operation type list) + an analogous screen for
 * currencies (ddd-model.md) — a single screen with three tabs: list with an active
 * flag, create, deactivate, delete.
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
  readonly behaviorKinds = signal<OperationBehaviorKind[]>([]);

  /**
   * Transfer is a system-only OperationBehaviorKind (created only as a pair of
   * transfer operations) — not offered to the user when creating a new operation type.
   */
  readonly creatableBehaviorKinds = computed(() => this.behaviorKinds().filter((kind) => kind.code !== 'Transfer'));

  /** ISO_CURRENCIES minus codes already present in the reference list — avoid offering duplicates. */
  readonly availableIsoCurrencies = computed(() => {
    const existingCodes = new Set(this.currencies().map((currency) => currency.code));
    return ISO_CURRENCIES.filter((currency) => !existingCodes.has(currency.code));
  });

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);

  readonly walletTypeForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
  });
  readonly operationTypeForm = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    behaviorKindId: ['', Validators.required],
  });
  readonly currencyForm = this.formBuilder.nonNullable.group({
    isoCode: ['', Validators.required],
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

    // includeInactive: true — unlike the mini-forms on the wallets screen (which only
    // need active items for selection), this management screen must also show
    // deactivated items with their active flag.
    forkJoin({
      walletTypes: this.referenceDataService.listWalletTypes({ includeInactive: true, limit: 100 }),
      operationTypes: this.referenceDataService.listOperationTypes({ includeInactive: true, limit: 100 }),
      currencies: this.referenceDataService.listCurrencies({ includeInactive: true, limit: 100 }),
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

    const isoCurrency = ISO_CURRENCIES.find((currency) => currency.code === this.currencyForm.getRawValue().isoCode);
    if (!isoCurrency) {
      return;
    }

    this.actionError.set(null);
    this.isSubmittingCurrency.set(true);

    this.referenceDataService.createCurrency(isoCurrency.code, isoCurrency.name).subscribe({
      next: () => {
        this.isSubmittingCurrency.set(false);
        this.currencyForm.reset({ isoCode: '' });
        this.reloadCurrencies();
      },
      error: (error: unknown) => {
        this.isSubmittingCurrency.set(false);
        this.actionError.set(extractErrorMessage(error, 'Не удалось создать валюту.'));
      },
    });
  }

  deactivateCurrency(item: Currency): void {
    this.actionError.set(null);
    this.referenceDataService.deactivateCurrency(item.id).subscribe({
      next: () => this.reloadCurrencies(),
      error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось деактивировать валюту.')),
    });
  }

  deleteCurrency(item: Currency): void {
    this.actionError.set(null);
    this.referenceDataService.deleteCurrency(item.id).subscribe({
      next: () => this.reloadCurrencies(),
      error: (error: unknown) =>
        this.actionError.set(extractErrorMessage(error, 'Не удалось удалить валюту: возможно, она уже используется.')),
    });
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

  private reloadCurrencies(): void {
    this.referenceDataService
      .listCurrencies({ includeInactive: true, limit: 100 })
      .subscribe((page) => this.currencies.set(page.data));
  }
}
