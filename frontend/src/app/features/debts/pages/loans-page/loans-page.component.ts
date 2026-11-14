import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import {
  Creditor,
  Loan,
  LoanScheduleGenerateRequest,
  LoanScheduleItemInput,
  PaymentCreateRequest,
} from '../../../../data-access/debts/debts-api.models';
import { DebtsApiService } from '../../../../data-access/debts/debts-api.service';
import { Currency, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { Money, compareAmounts, formatMoney } from '@shared/money/money';
import { appendPage } from '@shared/pagination/cursor-page';
import {
  OBLIGATION_STATUS_LABELS,
  OBLIGATION_STATUS_TONES,
  ObligationFormValue,
  PaymentWalletOption,
  SCHEDULE_STATUS_LABELS,
  SCHEDULE_STATUS_TONES,
  paymentStateLabel,
} from '../../debts.models';
import { CreditorCreateDialogComponent } from '../../components/creditor-create-dialog/creditor-create-dialog.component';
import { LoanScheduleEditorComponent } from '../../components/loan-schedule-editor/loan-schedule-editor.component';
import { ObligationFormComponent } from '../../components/obligation-form/obligation-form.component';
import { PaymentFormComponent } from '../../components/payment-form/payment-form.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

type RowAction = 'pay' | 'details' | 'edit' | 'schedule' | 'cancel' | 'delete';

/**
 * Loans (#8, ADR-0010 §3, #13): the list with remaining amount, next payment and status; create/edit; the schedule
 * editor (manual items or "N equal monthly payments") with item statuses paid/partial/overdue/expected; a payment by a
 * regular expense from a wallet in the loan currency (В4). Interest is not modelled (В5).
 */
@Component({
  selector: 'app-loans',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    CreditorCreateDialogComponent,
    LoanScheduleEditorComponent,
    ObligationFormComponent,
    PaymentFormComponent,
    BadgeComponent,
    ButtonComponent,
    DialogComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    FieldComponent,
    LoadingStateComponent,
    SelectComponent,
    SelectOptionDirective,
    SkeletonComponent,
    ToastComponent,
  ],
  templateUrl: './loans-page.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoansComponent implements OnInit {
  private readonly debtsService = inject(DebtsApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly statusLabels = OBLIGATION_STATUS_LABELS;
  protected readonly statusTones = OBLIGATION_STATUS_TONES;
  protected readonly scheduleLabels = SCHEDULE_STATUS_LABELS;
  protected readonly scheduleTones = SCHEDULE_STATUS_TONES;
  protected readonly paymentStateLabel = paymentStateLabel;

  private readonly obligationFormRef = viewChild(ObligationFormComponent);

  readonly creditors = signal<Creditor[]>([]);
  readonly currencies = signal<Currency[]>([]);
  readonly wallets = signal<Wallet[]>([]);
  readonly expenseTypes = signal<OperationType[]>([]);
  readonly lookupsError = signal<string | null>(null);

  readonly loans = signal<Loan[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly hasActiveFilter = signal(false);
  readonly filterForm = this.formBuilder.nonNullable.group({ creditorId: [''] });

  readonly successMessage = signal<string | null>(null);

  readonly formState = signal<{ loan: Loan | null } | null>(null);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly creditorDialogOpen = signal(false);

  readonly rowActionsLoan = signal<Loan | null>(null);
  private nextRowAction: { action: RowAction; loan: Loan } | null = null;
  /** The next dialog to open once the current form dialog has actually closed (never two dialogs switching at once). */
  private afterClose: (() => void) | null = null;

  readonly paymentLoan = signal<Loan | null>(null);
  readonly detailsLoan = signal<Loan | null>(null);
  readonly scheduleLoan = signal<Loan | null>(null);
  readonly confirmState = signal<{ action: 'cancel' | 'delete'; loan: Loan } | null>(null);
  readonly isConfirming = signal(false);
  readonly confirmError = signal<string | null>(null);

  readonly formInitial = computed<ObligationFormValue | null>(() => {
    const loan = this.formState()?.loan;
    return loan
      ? { creditorId: loan.creditorId, name: loan.name, currencyId: loan.currencyId, amount: loan.amountToRepay.amount, startDate: loan.startDate }
      : null;
  });

  readonly paymentWallets = computed<PaymentWalletOption[]>(() => {
    const loan = this.paymentLoan();
    if (!loan) {
      return [];
    }
    return this.wallets()
      .filter((wallet) => !wallet.isArchived && wallet.currencyId === loan.currencyId)
      .map((wallet) => ({ id: wallet.id, name: wallet.name, currencyId: wallet.currencyId, accountingStartDate: wallet.accountingStartDate }));
  });

  /** The next payment's outstanding amount, capped by the remaining amount — the prefilled payment amount. */
  readonly paymentDefaultAmount = computed<string | null>(() => {
    const loan = this.paymentLoan();
    const next = loan?.nextPayment?.outstandingAmount.amount;
    if (!loan || !next) {
      return null;
    }
    return compareAmounts(next, loan.remainingAmount.amount) > 0 ? loan.remainingAmount.amount : next;
  });

  ngOnInit(): void {
    this.loadLookups();
    this.loadLoans();
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

  loadLoans(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    const creditorId = this.filterForm.getRawValue().creditorId;
    this.hasActiveFilter.set(!!creditorId);
    this.debtsService.listLoans({ creditorId: creditorId || undefined }).subscribe({
      next: (page) => {
        this.loans.set(page.data);
        this.nextCursor.set(page.pagination.nextCursor);
        this.hasMore.set(page.pagination.hasMore);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить кредиты.'));
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
    this.debtsService.listLoans({ creditorId: creditorId || undefined, cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.loans(), page);
        this.loans.set(result.items);
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
    this.loadLoans();
  }

  openCreateForm(): void {
    this.submitError.set(null);
    this.formState.set({ loan: null });
  }

  closeForm(): void {
    if (!this.isSubmitting()) {
      this.formState.set(null);
      this.submitError.set(null);
      this.runAfterClose();
    }
  }

  onDismissRequested(event: Event): void {
    if (this.isSubmitting()) {
      event.preventDefault();
    }
  }

  saveLoan(value: ObligationFormValue): void {
    const state = this.formState();
    if (!state || this.isSubmitting()) {
      return;
    }
    const request$ = state.loan
      ? this.debtsService.updateLoan(state.loan.id, {
          creditorId: value.creditorId,
          name: value.name,
          amountToRepay: value.amount,
          startDate: value.startDate,
        })
      : this.debtsService.createLoan({
          creditorId: value.creditorId,
          name: value.name,
          currencyId: value.currencyId,
          amountToRepay: value.amount,
          startDate: value.startDate,
        });
    this.submit(request$, 'Не удалось сохранить кредит.', (loan) => {
      this.formState.set(null);
      if (state.loan) {
        this.successMessage.set('Кредит сохранён');
      } else {
        // A new loan has no schedule yet — continue straight to the schedule editor.
        this.successMessage.set('Кредит добавлен. Заполните график платежей');
        this.afterClose = () => this.scheduleLoan.set(loan);
      }
    });
  }

  onCreditorCreated(creditor: Creditor): void {
    this.creditors.update((list) => [...list, creditor].sort((a, b) => a.name.localeCompare(b.name, 'ru')));
    this.creditorDialogOpen.set(false);
    this.obligationFormRef()?.selectCreditor(creditor.id);
  }

  openRowActions(loan: Loan): void {
    this.rowActionsLoan.set(loan);
  }

  closeRowActions(): void {
    this.nextRowAction = null;
    this.rowActionsLoan.set(null);
  }

  chooseRowAction(action: RowAction, loan: Loan): void {
    this.nextRowAction = { action, loan };
    this.rowActionsLoan.set(null);
  }

  onRowActionsClosed(): void {
    const next = this.nextRowAction;
    this.nextRowAction = null;
    if (!next) {
      return;
    }
    this.submitError.set(null);
    switch (next.action) {
      case 'pay':
        this.paymentLoan.set(next.loan);
        break;
      case 'details':
        this.detailsLoan.set(next.loan);
        break;
      case 'edit':
        this.formState.set({ loan: next.loan });
        break;
      case 'schedule':
        this.scheduleLoan.set(next.loan);
        break;
      case 'cancel':
      case 'delete':
        this.confirmError.set(null);
        this.confirmState.set({ action: next.action, loan: next.loan });
        break;
    }
  }

  canDelete(loan: Loan): boolean {
    return loan.payments.length === 0;
  }

  closePayment(): void {
    if (!this.isSubmitting()) {
      this.paymentLoan.set(null);
      this.submitError.set(null);
    }
  }

  savePayment(request: PaymentCreateRequest): void {
    const loan = this.paymentLoan();
    if (!loan) {
      return;
    }
    this.submit(this.debtsService.recordLoanPayment(loan.id, request), 'Не удалось зафиксировать платёж.', (updated) => {
      this.paymentLoan.set(null);
      this.successMessage.set(updated.status === 'Repaid' ? 'Кредит погашен полностью' : 'Платёж учтён');
      this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe({ next: (page) => this.wallets.set(page.data), error: () => undefined });
    });
  }

  closeSchedule(): void {
    if (!this.isSubmitting()) {
      this.scheduleLoan.set(null);
      this.submitError.set(null);
      this.runAfterClose();
    }
  }

  replaceSchedule(items: LoanScheduleItemInput[]): void {
    const loan = this.scheduleLoan();
    if (loan) {
      this.submit(this.debtsService.replaceSchedule(loan.id, { items }), 'Не удалось сохранить график.', (updated) => {
        this.scheduleLoan.set(null);
        this.successMessage.set('График сохранён');
        this.afterClose = () => this.detailsLoan.set(updated);
      });
    }
  }

  generateSchedule(request: LoanScheduleGenerateRequest): void {
    const loan = this.scheduleLoan();
    if (loan) {
      this.submit(this.debtsService.generateSchedule(loan.id, request), 'Не удалось сгенерировать график.', (updated) => {
        this.scheduleLoan.set(null);
        this.successMessage.set(`График из ${request.numberOfPayments} платежей создан`);
        this.afterClose = () => this.detailsLoan.set(updated);
      });
    }
  }

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
      state.action === 'cancel' ? this.debtsService.cancelLoan(state.loan.id) : this.debtsService.deleteLoan(state.loan.id);
    request$.subscribe({
      next: () => {
        this.isConfirming.set(false);
        this.confirmState.set(null);
        this.successMessage.set(state.action === 'cancel' ? 'Кредит отменён' : 'Кредит удалён');
        this.loadLoans();
      },
      error: (error: unknown) => {
        this.isConfirming.set(false);
        this.confirmError.set(
          extractErrorMessage(error, state.action === 'cancel' ? 'Не удалось отменить кредит.' : 'Не удалось удалить кредит: возможно, по нему уже есть платежи.'),
        );
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.loadLoans();
        }
      },
    });
  }

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

  /** The schedule total differs from the amount to repay — shown as a warning in the details. */
  scheduleMismatch(loan: Loan): boolean {
    return loan.schedule.length > 0 && compareAmounts(loan.scheduledAmount.amount, loan.amountToRepay.amount) !== 0;
  }

  private runAfterClose(): void {
    const next = this.afterClose;
    this.afterClose = null;
    next?.();
  }

  private submit(request$: Observable<Loan>, fallback: string, onSuccess: (loan: Loan) => void): void {
    if (this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);
    request$.subscribe({
      next: (loan) => {
        this.isSubmitting.set(false);
        onSuccess(loan);
        this.loadLoans();
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(extractErrorMessage(error, fallback));
      },
    });
  }
}
