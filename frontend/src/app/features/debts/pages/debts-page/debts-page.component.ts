import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { Creditor, Debt, DebtSummary, PaymentCreateRequest } from '../../../../data-access/debts/debts-api.models';
import { DebtsApiService } from '../../../../data-access/debts/debts-api.service';
import { Currency, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { Money, formatMoney } from '@shared/money/money';
import { appendPage } from '@shared/pagination/cursor-page';
import { fieldError } from '@shared/forms/field-error';
import {
  OBLIGATION_STATUS_LABELS,
  OBLIGATION_STATUS_TONES,
  ObligationFormValue,
  PaymentWalletOption,
  paymentStateLabel,
} from '../../debts.models';
import { CreditorCreateDialogComponent } from '../../components/creditor-create-dialog/creditor-create-dialog.component';
import { ObligationFormComponent } from '../../components/obligation-form/obligation-form.component';
import { PaymentFormComponent } from '../../components/payment-form/payment-form.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { TabDefinition, TabsComponent } from '@shared/ui/tabs/tabs.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

type DebtsTab = 'debts' | 'creditors';
type RowAction = 'repay' | 'details' | 'edit' | 'cancel' | 'delete';

/**
 * Debts (#6, ADR-0010 §3, #13): the debt list with remaining amount and status, the summary by currency and creditor,
 * create/edit/cancel/delete, repayment by a regular expense from a wallet in the debt currency (В4) and the repayment
 * history. The second tab manages the creditor dictionary shared with loans (rename, deactivate, delete if unused).
 */
@Component({
  selector: 'app-debts',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CreditorCreateDialogComponent,
    ObligationFormComponent,
    PaymentFormComponent,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    DialogComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    FieldComponent,
    FieldControlDirective,
    LoadingStateComponent,
    SelectComponent,
    SelectOptionDirective,
    SkeletonComponent,
    TabsComponent,
    ToastComponent,
  ],
  templateUrl: './debts-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class DebtsComponent implements OnInit {
  private readonly debtsService = inject(DebtsApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly statusLabels = OBLIGATION_STATUS_LABELS;
  protected readonly statusTones = OBLIGATION_STATUS_TONES;
  protected readonly paymentStateLabel = paymentStateLabel;

  readonly tabs: TabDefinition[] = [
    { id: 'debts', label: 'Долги' },
    { id: 'creditors', label: 'Кредиторы' },
  ];
  readonly activeTab = signal<DebtsTab>('debts');

  private readonly obligationFormRef = viewChild(ObligationFormComponent);

  readonly creditors = signal<Creditor[]>([]);
  readonly currencies = signal<Currency[]>([]);
  readonly wallets = signal<Wallet[]>([]);
  readonly expenseTypes = signal<OperationType[]>([]);
  readonly lookupsError = signal<string | null>(null);

  readonly debts = signal<Debt[]>([]);
  readonly summary = signal<DebtSummary | null>(null);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly hasActiveFilter = signal(false);

  readonly filterForm = this.formBuilder.nonNullable.group({ creditorId: [''] });

  readonly successMessage = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);

  /** Create (`null` debt) or edit form. */
  readonly formState = signal<{ debt: Debt | null } | null>(null);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly creditorDialogOpen = signal(false);

  readonly rowActionsDebt = signal<Debt | null>(null);
  private nextRowAction: { action: RowAction; debt: Debt } | null = null;

  readonly paymentDebt = signal<Debt | null>(null);
  readonly detailsDebt = signal<Debt | null>(null);
  readonly confirmState = signal<{ action: 'cancel' | 'delete'; debt: Debt } | null>(null);
  readonly isConfirming = signal(false);
  readonly confirmError = signal<string | null>(null);

  // Creditors tab
  readonly creditorForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
  });
  readonly isAddingCreditor = signal(false);
  readonly renameCreditor = signal<Creditor | null>(null);
  readonly renameForm = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
  });
  readonly isRenaming = signal(false);
  readonly renameError = signal<string | null>(null);
  readonly deleteCreditorTarget = signal<Creditor | null>(null);
  readonly isDeletingCreditor = signal(false);
  readonly deleteCreditorError = signal<string | null>(null);

  readonly formInitial = computed<ObligationFormValue | null>(() => {
    const debt = this.formState()?.debt;
    return debt
      ? {
          creditorId: debt.creditorId,
          name: debt.name,
          currencyId: debt.currencyId,
          amount: debt.originalAmount.amount,
          startDate: debt.startDate,
        }
      : null;
  });

  readonly paymentWallets = computed<PaymentWalletOption[]>(() => {
    const debt = this.paymentDebt();
    return debt ? this.walletsInCurrency(debt.currencyId) : [];
  });

  ngOnInit(): void {
    this.loadLookups();
    this.loadDebts();
  }

  selectTab(id: string): void {
    this.activeTab.set(id as DebtsTab);
  }

  loadLookups(): void {
    this.lookupsError.set(null);
    forkJoin({
      creditors: this.debtsService.listCreditors({ includeInactive: true, limit: 100 }),
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      wallets: this.walletsService.list({ includeArchived: true, limit: 100 }),
      types: this.referenceDataService.listOperationTypes({ limit: 100 }),
      kinds: this.referenceDataService.listOperationBehaviorKinds(),
    }).subscribe({
      next: ({ creditors, currencies, wallets, types, kinds }) => {
        const expenseKindIds = new Set(kinds.filter((kind) => kind.code === 'Expense').map((kind) => kind.id));
        this.creditors.set(creditors.data);
        this.currencies.set(currencies.data);
        this.wallets.set(wallets.data);
        this.expenseTypes.set(types.data.filter((type) => type.isActive && expenseKindIds.has(type.behaviorKindId)));
      },
      error: (error: unknown) => this.lookupsError.set(extractErrorMessage(error, 'Не удалось загрузить кредиторов, кошельки и справочники.')),
    });
  }

  loadDebts(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    const creditorId = this.filterForm.getRawValue().creditorId;
    this.hasActiveFilter.set(!!creditorId);
    forkJoin({
      page: this.debtsService.listDebts({ creditorId: creditorId || undefined }),
      summary: this.debtsService.getDebtSummary(),
    }).subscribe({
      next: ({ page, summary }) => {
        this.debts.set(page.data);
        this.nextCursor.set(page.pagination.nextCursor);
        this.hasMore.set(page.pagination.hasMore);
        this.summary.set(summary);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить долги.'));
        this.isLoading.set(false);
      },
    });
  }

  loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.isLoading()) {
      return;
    }
    this.isLoading.set(true);
    const creditorId = this.filterForm.getRawValue().creditorId;
    this.debtsService.listDebts({ creditorId: creditorId || undefined, cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.debts(), page);
        this.debts.set(result.items);
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

  resetFilter(): void {
    this.filterForm.reset({ creditorId: '' });
    this.loadDebts();
  }

  // ---- create / edit ----

  openCreateForm(): void {
    this.submitError.set(null);
    this.formState.set({ debt: null });
  }

  closeForm(): void {
    if (this.isSubmitting()) {
      return;
    }
    this.formState.set(null);
    this.submitError.set(null);
  }

  onFormDismissRequested(event: Event): void {
    if (this.isSubmitting()) {
      event.preventDefault();
    }
  }

  saveDebt(value: ObligationFormValue): void {
    const state = this.formState();
    if (!state || this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);
    const request$ = state.debt
      ? this.debtsService.updateDebt(state.debt.id, {
          creditorId: value.creditorId,
          name: value.name,
          originalAmount: value.amount,
          startDate: value.startDate,
        })
      : this.debtsService.createDebt({
          creditorId: value.creditorId,
          name: value.name,
          currencyId: value.currencyId,
          originalAmount: value.amount,
          startDate: value.startDate,
        });
    request$.subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.formState.set(null);
        this.successMessage.set(state.debt ? 'Долг сохранён' : 'Долг добавлен');
        this.loadDebts();
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(extractErrorMessage(error, 'Не удалось сохранить долг.'));
      },
    });
  }

  onCreditorCreated(creditor: Creditor): void {
    this.creditors.update((list) => [...list, creditor].sort((a, b) => a.name.localeCompare(b.name, 'ru')));
    this.creditorDialogOpen.set(false);
    this.obligationFormRef()?.selectCreditor(creditor.id);
  }

  // ---- row actions ----

  openRowActions(debt: Debt): void {
    this.rowActionsDebt.set(debt);
  }

  closeRowActions(): void {
    this.nextRowAction = null;
    this.rowActionsDebt.set(null);
  }

  chooseRowAction(action: RowAction, debt: Debt): void {
    this.nextRowAction = { action, debt };
    this.rowActionsDebt.set(null);
  }

  /** Opens the chosen dialog only after the row-actions dialog has finished closing (Transfers/Operations pattern). */
  onRowActionsClosed(): void {
    const next = this.nextRowAction;
    this.nextRowAction = null;
    if (!next) {
      return;
    }
    this.actionError.set(null);
    switch (next.action) {
      case 'repay':
        this.submitError.set(null);
        this.paymentDebt.set(next.debt);
        break;
      case 'details':
        this.detailsDebt.set(next.debt);
        break;
      case 'edit':
        this.submitError.set(null);
        this.formState.set({ debt: next.debt });
        break;
      case 'cancel':
      case 'delete':
        this.confirmError.set(null);
        this.confirmState.set({ action: next.action, debt: next.debt });
        break;
    }
  }

  canDelete(debt: Debt): boolean {
    return debt.repayments.length === 0;
  }

  // ---- repayment ----

  closePayment(): void {
    if (!this.isSubmitting()) {
      this.paymentDebt.set(null);
      this.submitError.set(null);
    }
  }

  savePayment(request: PaymentCreateRequest): void {
    const debt = this.paymentDebt();
    if (!debt || this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);
    this.debtsService.recordRepayment(debt.id, request).subscribe({
      next: (updated) => {
        this.isSubmitting.set(false);
        this.paymentDebt.set(null);
        this.successMessage.set(updated.status === 'Repaid' ? 'Долг погашен полностью' : 'Погашение учтено');
        this.loadDebts();
        this.reloadWallets();
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(extractErrorMessage(error, 'Не удалось зафиксировать погашение.'));
      },
    });
  }

  // ---- cancel / delete ----

  closeConfirm(): void {
    if (!this.isConfirming()) {
      this.confirmState.set(null);
      this.confirmError.set(null);
    }
  }

  onConfirmDismissRequested(event: Event): void {
    if (this.isConfirming()) {
      event.preventDefault();
    }
  }

  confirm(): void {
    const state = this.confirmState();
    if (!state || this.isConfirming()) {
      return;
    }
    this.isConfirming.set(true);
    this.confirmError.set(null);
    const request$: Observable<unknown> =
      state.action === 'cancel' ? this.debtsService.cancelDebt(state.debt.id) : this.debtsService.deleteDebt(state.debt.id);
    request$.subscribe({
      next: () => {
        this.isConfirming.set(false);
        this.confirmState.set(null);
        this.successMessage.set(state.action === 'cancel' ? 'Долг отменён' : 'Долг удалён');
        this.loadDebts();
      },
      error: (error: unknown) => {
        this.isConfirming.set(false);
        this.confirmError.set(
          extractErrorMessage(error, state.action === 'cancel' ? 'Не удалось отменить долг.' : 'Не удалось удалить долг: возможно, по нему уже есть погашения.'),
        );
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.loadDebts();
        }
      },
    });
  }

  // ---- creditors tab ----

  creditorNameError(): string | null {
    return fieldError(this.creditorForm.controls.name, 'Укажите название');
  }

  addCreditor(): void {
    if (this.isAddingCreditor()) {
      return;
    }
    if (this.creditorForm.invalid) {
      this.creditorForm.markAllAsTouched();
      return;
    }
    this.isAddingCreditor.set(true);
    this.actionError.set(null);
    this.debtsService.createCreditor(this.creditorForm.getRawValue().name.trim()).subscribe({
      next: (creditor) => {
        this.isAddingCreditor.set(false);
        this.creditorForm.reset({ name: '' });
        this.creditors.update((list) => [...list, creditor].sort((a, b) => a.name.localeCompare(b.name, 'ru')));
        this.successMessage.set('Кредитор добавлен');
      },
      error: (error: unknown) => {
        this.isAddingCreditor.set(false);
        this.actionError.set(extractErrorMessage(error, 'Не удалось добавить кредитора.'));
      },
    });
  }

  openRename(creditor: Creditor): void {
    this.renameError.set(null);
    this.renameForm.reset({ name: creditor.name });
    this.renameCreditor.set(creditor);
  }

  closeRename(): void {
    if (!this.isRenaming()) {
      this.renameCreditor.set(null);
    }
  }

  submitRename(): void {
    const creditor = this.renameCreditor();
    if (!creditor || this.isRenaming()) {
      return;
    }
    if (this.renameForm.invalid) {
      this.renameForm.markAllAsTouched();
      return;
    }
    this.isRenaming.set(true);
    this.debtsService.renameCreditor(creditor.id, this.renameForm.getRawValue().name.trim()).subscribe({
      next: (updated) => {
        this.isRenaming.set(false);
        this.renameCreditor.set(null);
        this.creditors.update((list) => list.map((item) => (item.id === updated.id ? updated : item)));
        this.successMessage.set('Кредитор переименован');
        this.loadDebts();
      },
      error: (error: unknown) => {
        this.isRenaming.set(false);
        this.renameError.set(extractErrorMessage(error, 'Не удалось переименовать кредитора.'));
      },
    });
  }

  renameNameError(): string | null {
    return fieldError(this.renameForm.controls.name, 'Укажите название');
  }

  deactivateCreditor(creditor: Creditor): void {
    this.actionError.set(null);
    this.debtsService.deactivateCreditor(creditor.id).subscribe({
      next: (updated) => {
        this.creditors.update((list) => list.map((item) => (item.id === updated.id ? updated : item)));
        this.successMessage.set('Кредитор деактивирован');
      },
      error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось деактивировать кредитора.')),
    });
  }

  requestDeleteCreditor(creditor: Creditor): void {
    this.deleteCreditorError.set(null);
    this.deleteCreditorTarget.set(creditor);
  }

  closeDeleteCreditor(): void {
    if (!this.isDeletingCreditor()) {
      this.deleteCreditorTarget.set(null);
    }
  }

  confirmDeleteCreditor(): void {
    const creditor = this.deleteCreditorTarget();
    if (!creditor || this.isDeletingCreditor()) {
      return;
    }
    this.isDeletingCreditor.set(true);
    this.debtsService.deleteCreditor(creditor.id).subscribe({
      next: () => {
        this.isDeletingCreditor.set(false);
        this.deleteCreditorTarget.set(null);
        this.creditors.update((list) => list.filter((item) => item.id !== creditor.id));
        this.successMessage.set('Кредитор удалён');
      },
      error: (error: unknown) => {
        this.isDeletingCreditor.set(false);
        this.deleteCreditorError.set(
          extractErrorMessage(error, 'Не удалось удалить кредитора: он используется в долге или кредите. Его можно деактивировать.'),
        );
      },
    });
  }

  // ---- display helpers ----

  creditorName(id: string): string {
    return this.creditors().find((creditor) => creditor.id === id)?.name ?? '—';
  }

  currencyCode(id: string): string {
    return this.currencies().find((currency) => currency.id === id)?.code ?? '';
  }

  walletName(id: string): string {
    return this.wallets().find((wallet) => wallet.id === id)?.name ?? 'Кошелёк недоступен';
  }

  money(value: Money): string {
    return formatMoney(value, value.currencyCode ?? this.currencyCode(value.currencyId));
  }

  private walletsInCurrency(currencyId: string): PaymentWalletOption[] {
    return this.wallets()
      .filter((wallet) => !wallet.isArchived && wallet.currencyId === currencyId)
      .map((wallet) => ({ id: wallet.id, name: wallet.name, currencyId: wallet.currencyId, accountingStartDate: wallet.accountingStartDate }));
  }

  private reloadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe({ next: (page) => this.wallets.set(page.data), error: () => undefined });
  }
}
