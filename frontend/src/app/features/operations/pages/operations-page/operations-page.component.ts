import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { appendPage } from '@shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '@shared/money/money';
import { OperationBehaviorKind, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { CreateOperationRequest, Operation, UpdateOperationRequest } from '../../../../data-access/operations/operations-api.models';
import { OperationsApiService } from '../../../../data-access/operations/operations-api.service';
import { OperationFormComponent, WalletOption } from '../../components/operation-form/operation-form.component';
import { OperationTypeOption } from '../../operations.models';
import { BadgeComponent, BadgeTone } from '@shared/ui/badge/badge.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

/**
 * UC-11 (income), UC-12 (expense), UC-13 (edit), UC-14 (delete), UC-15 (list),
 * UC-24 (Absolute/Delta adjustment, Q3). Operations that are part of a transfer
 * (transferId !== null, UC-17) are shown as blocked from direct edit/delete —
 * they are managed from the "Transfers" screen (see features/transfers).
 */
@Component({
  selector: 'app-operations',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    OperationFormComponent,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    FieldComponent,
    FieldControlDirective,
    LoadingStateComponent,
    SkeletonComponent,
    ToastComponent,
  ],
  templateUrl: './operations-page.component.html',
  styleUrl: './operations-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationsComponent implements OnInit {
  private readonly operationsService = inject(OperationsApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
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
  readonly successMessage = signal<string | null>(null);
  readonly editingOperation = signal<Operation | null>(null);

  readonly deleteError = signal<string | null>(null);

  readonly filterForm = this.formBuilder.nonNullable.group({
    walletId: [''],
    operationTypeId: [''],
    dateFrom: [''],
    dateTo: [''],
  });

  /** Wallets for the form/filter dropdowns. */
  readonly walletOptions = computed<WalletOption[]>(() =>
    this.wallets().map((wallet) => ({ id: wallet.id, name: wallet.name })),
  );

  /** All active operation types enriched with the behaviorKind code — for the filter. */
  readonly allOperationTypeOptions = computed<OperationTypeOption[]>(() => {
    const kinds = new Map(this.behaviorKinds().map((kind) => [kind.id, kind.code]));
    return this.operationTypesRaw().map((type) => ({
      id: type.id,
      name: type.name,
      behaviorKindCode: kinds.get(type.behaviorKindId) ?? '',
    }));
  });

  /** Types allowed for creation via /operations — Transfer is only created via /transfers. */
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
      // Part of a transfer — editable only via the "Transfers" screen (UC-17).
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
        this.successMessage.set(editing ? 'Операция изменена' : 'Операция создана');
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
      next: () => {
        this.loadOperations();
        this.successMessage.set('Операция удалена');
      },
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

  /** Operation type's behaviorKind code — used to style the row (ui-kit §5 "Table / list row"). */
  operationBehaviorKindCode(operationTypeId: string): string {
    return this.allOperationTypeOptions().find((type) => type.id === operationTypeId)?.behaviorKindCode ?? '';
  }

  /** Badge tone for the operation type: color is not the only cue — the operation type's text label also serves as one. */
  operationTone(behaviorKindCode: string): BadgeTone {
    if (behaviorKindCode === 'Income') {
      return 'success';
    }
    if (behaviorKindCode === 'Expense') {
      return 'danger';
    }
    return 'neutral';
  }

  /** Amount sign: "+" for income, "−" for expense (ui-kit §5) — not shown for other types (e.g. adjustment). */
  amountSign(behaviorKindCode: string): '+' | '−' | '' {
    if (behaviorKindCode === 'Income') {
      return '+';
    }
    if (behaviorKindCode === 'Expense') {
      return '−';
    }
    return '';
  }

  /**
   * Text label for the amount's direction (ui-kit §2 "Color does not replace text"
   * and §5: "income: '+' sign and label; expense: '−' sign and label"). The specific
   * operation type name (badge) stays separate — this label just duplicates the
   * direction as a word next to the sign, without replacing the type-specific name.
   */
  amountCategoryLabel(behaviorKindCode: string): string {
    if (behaviorKindCode === 'Income') {
      return 'доход';
    }
    if (behaviorKindCode === 'Expense') {
      return 'расход';
    }
    return '';
  }
}
