import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { AbstractControl, FormBuilder, ReactiveFormsModule, ValidationErrors, ValidatorFn } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { appendPage } from '@shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '@shared/money/money';
import { Currency, OperationBehaviorKind, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
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
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
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
 *
 * ADR-0002 §3/§5: deletion is soft and allowed for any date. A deleted operation
 * stays in the list (row class `is-deleted` + "Удалена" badge), cannot be edited
 * and can be restored without confirmation (the action is reversible).
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
    DialogComponent,
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
  readonly currencies = signal<Currency[]>([]);
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

  /** Error of the last delete attempt — shown inside the confirmation dialog. */
  readonly deleteError = signal<string | null>(null);
  readonly isDeleting = signal(false);
  /** Error of the last restore attempt — shown as an error toast (restore has no confirm dialog). */
  readonly restoreError = signal<string | null>(null);
  readonly isRestoring = signal(false);
  /** UC-14: deletion is soft (the record stays in the list and can be restored), but it still changes
   *  balances — require confirmation (mirrors Wallets' archive/delete confirm). */
  readonly pendingDeleteOperation = signal<Operation | null>(null);
  /** screen-spec.md "Операции": действия строки находятся под «···». */
  readonly pendingRowActionsOperation = signal<Operation | null>(null);

  readonly filterForm = this.formBuilder.nonNullable.group(
    {
      walletId: [''],
      operationTypeId: [''],
      dateFrom: [''],
      dateTo: [''],
    },
    { validators: reverseDateRangeValidator },
  );

  /** screen-spec.md "Операции": пустой результат фильтра предлагает сброс, а не
   *  общий пустой список — отдельно от `operations().length === 0`, т.к. само по
   *  себе оно не говорит, применены ли сейчас фильтры. Снимок на момент последней
   *  успешно применённой загрузки, а не текущее (возможно, ещё не отправленное)
   *  состояние `filterForm`. */
  readonly hasActiveFilters = signal(false);

  /** Wallets for the form/filter dropdowns. */
  readonly walletOptions = computed<WalletOption[]>(() => {
    const currencies = new Map(this.currencies().map((currency) => [currency.id, currency.code]));
    return this.wallets().map((wallet) => ({
      id: wallet.id,
      name: wallet.name,
      currencyCode: currencies.get(wallet.currencyId) ?? '',
    }));
  });

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
    this.loadCurrencies();
    this.loadOperationTypes();
    this.loadBehaviorKinds();
    this.loadOperations();
  }

  loadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
  }

  loadCurrencies(): void {
    this.referenceDataService.listCurrencies({ limit: 100 }).subscribe((page) => this.currencies.set(page.data));
  }

  loadOperationTypes(): void {
    this.referenceDataService.listOperationTypes({ limit: 100 }).subscribe((page) => this.operationTypesRaw.set(page.data));
  }

  loadBehaviorKinds(): void {
    this.referenceDataService.listOperationBehaviorKinds().subscribe((kinds) => this.behaviorKinds.set(kinds));
  }

  loadOperations(): void {
    if (this.filterForm.invalid) {
      // screen-spec.md "Операции": обратный диапазон дат показывает ошибку, а не запрос.
      return;
    }

    this.isLoading.set(true);
    this.loadError.set(null);

    const filters = this.filterForm.getRawValue();
    this.hasActiveFilters.set(!!(filters.walletId || filters.operationTypeId || filters.dateFrom || filters.dateTo));
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
    if (operation.transferId || operation.isDeleted) {
      // Part of a transfer — editable only via the "Transfers" screen (UC-17);
      // a deleted operation must be restored first (ADR-0002 §3).
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

  /**
   * "Изменить"/"Удалить" inside the row-actions dialog must open the *next* dialog
   * only after this one has actually finished closing (`onRowActionsDialogClosed`,
   * wired to `app-dialog`'s `(closed)`) — not synchronously here. Both dialogs are
   * independent `DialogComponent` instances, each reacting to its own `[open]`
   * input via its own `effect()`; opening one while the other is still mid-close
   * in the same tick makes their effects' relative flush order (unspecified)
   * decide whether focus lands in the new dialog or gets stolen back to the
   * "···" button by the closing one's focus-restore.
   */
  private nextRowAction: { type: 'edit' | 'delete' | 'restore'; operation: Operation } | null = null;

  openRowActions(operation: Operation): void {
    this.pendingRowActionsOperation.set(operation);
  }

  closeRowActions(): void {
    this.nextRowAction = null;
    this.pendingRowActionsOperation.set(null);
  }

  editFromRowActions(operation: Operation): void {
    this.nextRowAction = { type: 'edit', operation };
    this.pendingRowActionsOperation.set(null);
  }

  deleteFromRowActions(operation: Operation): void {
    this.nextRowAction = { type: 'delete', operation };
    this.pendingRowActionsOperation.set(null);
  }

  restoreFromRowActions(operation: Operation): void {
    this.nextRowAction = { type: 'restore', operation };
    this.pendingRowActionsOperation.set(null);
  }

  onRowActionsDialogClosed(): void {
    const next = this.nextRowAction;
    this.nextRowAction = null;
    if (next?.type === 'edit') {
      this.openEditForm(next.operation);
    } else if (next?.type === 'delete') {
      this.requestDeleteConfirm(next.operation);
    } else if (next?.type === 'restore') {
      this.restoreOperation(next.operation);
    }
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

  requestDeleteConfirm(operation: Operation): void {
    if (operation.transferId || operation.isDeleted) {
      return;
    }
    this.deleteError.set(null);
    this.restoreError.set(null);
    this.pendingDeleteOperation.set(operation);
  }

  cancelDeleteConfirm(): void {
    if (this.isDeleting()) {
      return;
    }
    this.pendingDeleteOperation.set(null);
    this.deleteError.set(null);
  }

  /** Escape/backdrop on the delete-confirm dialog (native `cancel` event): while a DELETE request is
   *  in flight the native close must be prevented, otherwise an error would have no visible place to render. */
  onDeleteDismissRequested(event: Event): void {
    if (this.isDeleting()) {
      event.preventDefault();
      return;
    }
    this.pendingDeleteOperation.set(null);
    this.deleteError.set(null);
  }

  confirmDeleteOperation(): void {
    const operation = this.pendingDeleteOperation();
    if (!operation || this.isDeleting()) {
      return;
    }
    this.isDeleting.set(true);
    this.deleteError.set(null);

    this.operationsService.delete(operation.id).subscribe({
      next: () => {
        this.isDeleting.set(false);
        this.pendingDeleteOperation.set(null);
        this.loadOperations();
        this.successMessage.set('Операция удалена');
      },
      error: (error: unknown) => {
        this.isDeleting.set(false);
        this.deleteError.set(extractErrorMessage(error, 'Не удалось удалить операцию.'));
        this.reloadOnConflict(error);
      },
    });
  }

  /** ADR-0002 §3: restore is reversible, so it runs without a confirmation dialog. */
  restoreOperation(operation: Operation): void {
    if (operation.transferId || !operation.isDeleted || this.isRestoring()) {
      return;
    }
    this.isRestoring.set(true);
    this.deleteError.set(null);
    this.restoreError.set(null);

    this.operationsService.restore(operation.id).subscribe({
      next: () => {
        this.isRestoring.set(false);
        this.loadOperations();
        this.successMessage.set('Операция восстановлена');
      },
      error: (error: unknown) => {
        this.isRestoring.set(false);
        this.restoreError.set(extractErrorMessage(error, 'Не удалось восстановить операцию.'));
        this.reloadOnConflict(error);
      },
    });
  }

  /** The record was changed from another tab (409: already deleted / not deleted / modified
   *  concurrently) — the list on screen is stale, so refresh it. */
  private reloadOnConflict(error: unknown): void {
    if (error instanceof HttpErrorResponse && error.status === 409) {
      this.loadOperations();
    }
  }

  /** Tooltip for the "Удалена" badge: the local deletion date. */
  deletedBadgeTitle(operation: Operation): string {
    return operation.deletedAt ? `Удалена ${new Date(operation.deletedAt).toLocaleDateString('ru-RU')}` : 'Удалена';
  }

  /** Accessible name of the row-actions button: date, type and amount identify the row for screen readers. */
  rowActionsLabel(operation: Operation): string {
    const behaviorKindCode = this.operationBehaviorKindCode(operation.operationTypeId);
    const amount = this.amountSign(behaviorKindCode) + formatMoney(operation.amount, this.walletCurrencyCode(operation.walletId));
    return `Действия с операцией: ${operation.operationDate}, ${this.operationTypeName(operation.operationTypeId)}, ${amount}`;
  }

  walletName(walletId: string): string {
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? walletId;
  }

  /** Currency code by wallet id — for showing an amount with its currency explicitly (ui-kit §5). */
  walletCurrencyCode(walletId: string): string | undefined {
    const currencyId = this.wallets().find((wallet) => wallet.id === walletId)?.currencyId;
    return this.currencies().find((currency) => currency.id === currencyId)?.code;
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

/** screen-spec.md "Операции": обратный диапазон дат показывает ошибку. */
const reverseDateRangeValidator: ValidatorFn = (group: AbstractControl): ValidationErrors | null => {
  const dateFrom = group.get('dateFrom')?.value;
  const dateTo = group.get('dateTo')?.value;
  return dateFrom && dateTo && dateFrom > dateTo ? { reverseDateRange: true } : null;
};
