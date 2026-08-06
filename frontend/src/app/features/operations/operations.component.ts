import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { appendPage } from '../../core/api/cursor-page';
import { extractErrorMessage } from '../../core/http/problem-details';
import { formatMoney } from '../../core/money/money';
import { OperationBehaviorKind, OperationType } from '../reference-data/reference-data.models';
import { ReferenceDataService } from '../reference-data/reference-data.service';
import { Wallet } from '../wallets/wallets.models';
import { WalletsService } from '../wallets/wallets.service';
import { OperationFormComponent, WalletOption } from './operation-form/operation-form.component';
import { CreateOperationRequest, Operation, OperationTypeOption, UpdateOperationRequest } from './operations.models';
import { OperationsService } from './operations.service';

/**
 * UC-11 (доход), UC-12 (расход), UC-13 (редактирование), UC-14 (удаление),
 * UC-15 (просмотр списка), UC-24 (корректировка Absolute/Delta, Q3).
 * Операции-части переводов (transferId !== null, UC-17) отображаются как
 * заблокированные для правки/удаления напрямую — управляются со экрана
 * "Переводы" (см. features/transfers).
 */
@Component({
  selector: 'app-operations',
  standalone: true,
  imports: [ReactiveFormsModule, RouterLink, OperationFormComponent],
  templateUrl: './operations.component.html',
  styleUrl: './operations.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationsComponent implements OnInit {
  private readonly operationsService = inject(OperationsService);
  private readonly walletsService = inject(WalletsService);
  private readonly referenceDataService = inject(ReferenceDataService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly formatMoney = formatMoney;

  readonly wallets = signal<Wallet[]>([]);
  readonly operationTypesRaw = signal<OperationType[]>([]);
  readonly behaviorKinds = signal<OperationBehaviorKind[]>([]);

  readonly operations = signal<Operation[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly isFormOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly editingOperation = signal<Operation | null>(null);

  readonly deleteError = signal<string | null>(null);

  readonly filterForm = this.formBuilder.nonNullable.group({
    walletId: [''],
    operationTypeId: [''],
    dateFrom: [''],
    dateTo: [''],
  });

  /** Кошельки для выпадающих списков формы/фильтра. */
  readonly walletOptions = computed<WalletOption[]>(() =>
    this.wallets().map((wallet) => ({ id: wallet.id, name: wallet.name })),
  );

  /** Все активные типы операций, дополненные кодом поведения — для фильтра. */
  readonly allOperationTypeOptions = computed<OperationTypeOption[]>(() => {
    const kinds = new Map(this.behaviorKinds().map((kind) => [kind.id, kind.code]));
    return this.operationTypesRaw().map((type) => ({
      id: type.id,
      name: type.name,
      behaviorKindCode: kinds.get(type.behaviorKindId) ?? '',
    }));
  });

  /** Типы, допустимые для создания через /operations — Transfer создается только через /transfers. */
  readonly creatableOperationTypeOptions = computed<OperationTypeOption[]>(() =>
    this.allOperationTypeOptions().filter((type) => type.behaviorKindCode !== 'Transfer'),
  );

  ngOnInit(): void {
    this.loadWallets();
    this.loadOperationTypes();
    this.loadBehaviorKinds();
    this.loadOperations();
  }

  loadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
  }

  loadOperationTypes(): void {
    this.referenceDataService.listOperationTypes({ limit: 100 }).subscribe((page) => this.operationTypesRaw.set(page.data));
  }

  loadBehaviorKinds(): void {
    this.referenceDataService.listOperationBehaviorKinds().subscribe((kinds) => this.behaviorKinds.set(kinds));
  }

  loadOperations(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    const filters = this.filterForm.getRawValue();
    this.operationsService
      .list({
        walletId: filters.walletId || undefined,
        operationTypeId: filters.operationTypeId || undefined,
        dateFrom: filters.dateFrom || undefined,
        dateTo: filters.dateTo || undefined,
      })
      .subscribe({
        next: (page) => {
          this.operations.set(page.data);
          this.nextCursor.set(page.pagination.nextCursor);
          this.hasMore.set(page.pagination.hasMore);
          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить список операций.'));
          this.isLoading.set(false);
        },
      });
  }

  applyFilters(): void {
    this.loadOperations();
  }

  resetFilters(): void {
    this.filterForm.reset({ walletId: '', operationTypeId: '', dateFrom: '', dateTo: '' });
    this.loadOperations();
  }

  loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.isLoading()) {
      return;
    }

    this.isLoading.set(true);
    const filters = this.filterForm.getRawValue();
    this.operationsService
      .list({
        walletId: filters.walletId || undefined,
        operationTypeId: filters.operationTypeId || undefined,
        dateFrom: filters.dateFrom || undefined,
        dateTo: filters.dateTo || undefined,
        cursor,
      })
      .subscribe({
        next: (page) => {
          const result = appendPage(this.operations(), page);
          this.operations.set(result.items);
          this.nextCursor.set(result.nextCursor);
          this.hasMore.set(result.hasMore);
          this.isLoading.set(false);
        },
        error: (error: unknown) => {
          this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить следующую страницу.'));
          this.isLoading.set(false);
        },
      });
  }

  openCreateForm(): void {
    this.editingOperation.set(null);
    this.submitError.set(null);
    this.isFormOpen.set(true);
  }

  openEditForm(operation: Operation): void {
    if (operation.transferId) {
      // Часть перевода — редактирование только через экран "Переводы" (UC-17).
      return;
    }
    this.editingOperation.set(operation);
    this.submitError.set(null);
    this.isFormOpen.set(true);
  }

  cancelForm(): void {
    this.isFormOpen.set(false);
    this.editingOperation.set(null);
    this.submitError.set(null);
  }

  saveOperation(request: CreateOperationRequest | UpdateOperationRequest): void {
    if (this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);

    const editing = this.editingOperation();
    const request$ = editing
      ? this.operationsService.update(editing.id, request as UpdateOperationRequest)
      : this.operationsService.create(request as CreateOperationRequest);

    request$.subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.cancelForm();
        this.loadOperations();
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(
          extractErrorMessage(error, editing ? 'Не удалось изменить операцию.' : 'Не удалось создать операцию.'),
        );
      },
    });
  }

  deleteOperation(operation: Operation): void {
    if (operation.transferId) {
      return;
    }
    this.deleteError.set(null);

    this.operationsService.delete(operation.id).subscribe({
      next: () => this.loadOperations(),
      error: (error: unknown) => {
        this.deleteError.set(
          extractErrorMessage(
            error,
            'Не удалось удалить операцию: возможно, она уже учтена в истории баланса — отредактируйте её вместо удаления.',
          ),
        );
      },
    });
  }

  walletName(walletId: string): string {
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? walletId;
  }

  operationTypeName(operationTypeId: string): string {
    return this.operationTypesRaw().find((type) => type.id === operationTypeId)?.name ?? operationTypeId;
  }
}
