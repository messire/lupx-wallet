import { Component, computed, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { Currency, OperationBehaviorKind, OperationType, ReferenceItem } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';

export type ReferenceTab = 'wallet-types' | 'operation-types' | 'currencies';

/**
 * UC-26 (список типов кошельков), UC-27 (список типов операций) + аналогичный экран
 * для валют (ddd-model.md) — единый экран с тремя вкладками: список с признаком
 * активности, создание, деактивация, удаление (docs/PROGRESS.md, W1.4).
 *
 * HTTP-вызовы идут только через ReferenceDataApiService — тот же сервис, что используют
 * мини-формы быстрого добавления на экране кошельков (features/wallets), логика не
 * дублируется.
 */
@Component({
  selector: 'app-reference-data',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './reference-data.component.html',
  styleUrl: './reference-data.component.scss',
})
export class ReferenceDataComponent implements OnInit {
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  readonly activeTab = signal<ReferenceTab>('wallet-types');

  readonly walletTypes = signal<ReferenceItem[]>([]);
  readonly operationTypes = signal<OperationType[]>([]);
  readonly currencies = signal<Currency[]>([]);
  readonly behaviorKinds = signal<OperationBehaviorKind[]>([]);

  /**
   * Transfer — служебный OperationBehaviorKind (создается только парой операций
   * перевода, см. docs/PROGRESS.md, баг "OperationEffectCalculator падал... при
   * попытке создать операцию с поведением Transfer напрямую") — не предлагается
   * пользователю при создании нового типа операции.
   */
  readonly creatableBehaviorKinds = computed(() => this.behaviorKinds().filter((kind) => kind.code !== 'Transfer'));

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
    code: ['', Validators.required],
    name: ['', Validators.required],
  });

  readonly isSubmittingWalletType = signal(false);
  readonly isSubmittingOperationType = signal(false);
  readonly isSubmittingCurrency = signal(false);

  ngOnInit(): void {
    this.loadAll();
  }

  selectTab(tab: ReferenceTab): void {
    this.activeTab.set(tab);
    this.actionError.set(null);
  }

  loadAll(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    // includeInactive: true — в отличие от мини-форм на экране кошельков (которым
    // нужны только активные элементы для выбора), этот экран управления должен
    // показывать и деактивированные элементы с признаком активности.
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

    this.actionError.set(null);
    this.isSubmittingCurrency.set(true);
    const { code, name } = this.currencyForm.getRawValue();

    this.referenceDataService.createCurrency(code, name).subscribe({
      next: () => {
        this.isSubmittingCurrency.set(false);
        this.currencyForm.reset({ code: '', name: '' });
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
