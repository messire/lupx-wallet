import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import {
  PlanStatus,
  PlannedOccurrence,
  PlannedOccurrenceExecuteRequest,
  PlannedTransaction,
  PlannedTransactionWriteRequest,
} from '../../../../data-access/planning/planning-api.models';
import { PlanningApiService } from '../../../../data-access/planning/planning-api.service';
import { UpcomingPayment } from '../../../../data-access/reporting/reporting-api.models';
import { ReportingApiService } from '../../../../data-access/reporting/reporting-api.service';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { Money, addAmounts, amountValidator, formatMoney, positiveAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { appendPage } from '@shared/pagination/cursor-page';
import { formatDateText, formatMonthLabel, monthRange, shiftMonth, todayIsoDate } from '@shared/ui/date-input/calendar-date';
import {
  OCCURRENCE_STATUS_LABELS,
  OCCURRENCE_STATUS_TONES,
  PlanWalletOption,
  isExecutable,
  occurrenceDisplayStatus,
  recurrenceLabel,
} from '../../planning.models';
import { OccurrenceExecuteFormComponent } from '../../components/occurrence-execute-form/occurrence-execute-form.component';
import { PlanFormComponent, PlanTypeOption } from '../../components/plan-form/plan-form.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DateInputComponent } from '@shared/ui/date-input/date-input.component';
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

type PlanningTab = 'occurrences' | 'plans';
type OccurrenceAction = 'execute' | 'move' | 'amount';
type PlanAction = 'occurrences' | 'edit' | 'finish' | 'delete';

/** Expected sums of a period per currency and direction. */
interface PeriodTotal {
  currencyId: string;
  income: string;
  expense: string;
}

/**
 * Planned transactions (#9, ADR-0010 §6, #13). The «Календарь» tab lists the computed occurrences of a month
 * (expected / partial / executed / skipped / overdue) with actions skip/unskip, move, change amount and execute
 * (a prefilled form that creates a regular operation). The «Планы» tab lists the plans with their recurrence and
 * supports create/edit/finish/delete. Until executed a plan changes neither balance nor balance history.
 */
@Component({
  selector: 'app-planning',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    OccurrenceExecuteFormComponent,
    PlanFormComponent,
    BadgeComponent,
    ButtonComponent,
    DateInputComponent,
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
  templateUrl: './planning-page.component.html',
  styleUrl: './planning-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlanningComponent implements OnInit {
  private readonly planningService = inject(PlanningApiService);
  private readonly reportingService = inject(ReportingApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly statusLabels = OCCURRENCE_STATUS_LABELS;
  protected readonly statusTones = OCCURRENCE_STATUS_TONES;
  protected readonly displayStatus = occurrenceDisplayStatus;
  protected readonly isExecutable = isExecutable;
  protected readonly recurrenceLabel = recurrenceLabel;
  protected readonly formatDateText = formatDateText;
  protected readonly formatMonthLabel = formatMonthLabel;

  readonly tabs: TabDefinition[] = [
    { id: 'occurrences', label: 'Календарь' },
    { id: 'plans', label: 'Планы' },
  ];
  readonly activeTab = signal<PlanningTab>('occurrences');

  readonly currencies = signal<Currency[]>([]);
  readonly wallets = signal<Wallet[]>([]);
  readonly typeOptions = signal<PlanTypeOption[]>([]);
  readonly lookupsError = signal<string | null>(null);

  // occurrences
  readonly month = signal(todayIsoDate().slice(0, 7));
  readonly occurrences = signal<PlannedOccurrence[]>([]);
  readonly occurrencesLoading = signal(false);
  readonly occurrencesError = signal<string | null>(null);
  readonly occurrenceFilter = this.formBuilder.nonNullable.group({ planId: [''] });
  readonly rowActionsOccurrence = signal<PlannedOccurrence | null>(null);
  private nextOccurrenceAction: { action: OccurrenceAction; occurrence: PlannedOccurrence } | null = null;
  readonly executeOccurrence = signal<PlannedOccurrence | null>(null);
  readonly moveOccurrence = signal<PlannedOccurrence | null>(null);
  readonly amountOccurrence = signal<PlannedOccurrence | null>(null);
  readonly moveForm = this.formBuilder.nonNullable.group({ date: ['', Validators.required] });
  readonly amountForm = this.formBuilder.nonNullable.group({
    amount: ['', [Validators.required, amountValidator, positiveAmountValidator]],
  });

  // plans
  readonly plans = signal<PlannedTransaction[]>([]);
  readonly plansNextCursor = signal<string | null>(null);
  readonly plansHasMore = signal(false);
  readonly plansLoading = signal(false);
  readonly plansError = signal<string | null>(null);
  readonly planFilter = this.formBuilder.nonNullable.group({ status: ['Active' as PlanStatus | ''] });
  readonly rowActionsPlan = signal<PlannedTransaction | null>(null);
  private nextPlanAction: { action: PlanAction; plan: PlannedTransaction } | null = null;
  readonly planFormState = signal<{ plan: PlannedTransaction | null } | null>(null);
  readonly confirmState = signal<{ action: 'finish' | 'delete'; plan: PlannedTransaction } | null>(null);

  readonly successMessage = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);

  /** Every plan known to the page (for names, statuses and the occurrence filter), independent of the plans tab filter. */
  readonly allPlans = signal<PlannedTransaction[]>([]);

  readonly periodTotals = computed<PeriodTotal[]>(() => {
    const totals = new Map<string, PeriodTotal>();
    for (const occurrence of this.occurrences()) {
      const currencyId = occurrence.expectedAmount.currencyId;
      const total = totals.get(currencyId) ?? { currencyId, income: '0', expense: '0' };
      if (occurrence.direction === 'Income') {
        total.income = addAmounts(total.income, occurrence.expectedAmount.amount);
      } else {
        total.expense = addAmounts(total.expense, occurrence.expectedAmount.amount);
      }
      totals.set(currencyId, total);
    }
    return [...totals.values()];
  });

  readonly overdueCount = computed(() => this.occurrences().filter((occurrence) => occurrence.isOverdue).length);

  /**
   * Overdue planned expenses of other months (GET /reporting/upcoming-payments, up to 366 days back): the calendar
   * shows one month, so without this an occurrence forgotten last month would stay unnoticed. Planned income is not
   * in that report (it lists payments only).
   */
  private readonly overduePayments = signal<UpcomingPayment[]>([]);
  readonly overdueElsewhere = computed(() => {
    const month = this.month();
    const planId = this.occurrenceFilter.controls.planId.value;
    const counts = new Map<string, number>();
    for (const item of this.overduePayments()) {
      const itemMonth = item.date.slice(0, 7);
      if (itemMonth === month || (planId && item.plannedTransactionId !== planId)) {
        continue;
      }
      counts.set(itemMonth, (counts.get(itemMonth) ?? 0) + 1);
    }
    return [...counts.entries()].sort(([a], [b]) => a.localeCompare(b)).map(([itemMonth, count]) => ({ month: itemMonth, count }));
  });

  readonly executeWallets = computed<PlanWalletOption[]>(() => {
    const occurrence = this.executeOccurrence();
    return occurrence ? this.walletOptions().filter((wallet) => wallet.currencyId === occurrence.plannedAmount.currencyId) : [];
  });

  readonly walletOptions = computed<PlanWalletOption[]>(() =>
    this.wallets()
      .filter((wallet) => !wallet.isArchived)
      .map((wallet) => ({ id: wallet.id, name: wallet.name, currencyId: wallet.currencyId, accountingStartDate: wallet.accountingStartDate })),
  );

  constructor() {
    this.occurrenceFilter.controls.planId.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.loadOccurrences());
    this.planFilter.controls.status.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.loadPlans());
  }

  ngOnInit(): void {
    this.loadLookups();
    this.loadOccurrences();
    this.loadPlans();
  }

  selectTab(id: string): void {
    this.activeTab.set(id as PlanningTab);
  }

  loadLookups(): void {
    this.lookupsError.set(null);
    forkJoin({
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      wallets: this.walletsService.list({ includeArchived: true, limit: 100 }),
      types: this.referenceDataService.listOperationTypes({ includeInactive: true, limit: 100 }),
      kinds: this.referenceDataService.listOperationBehaviorKinds(),
      plans: this.planningService.list({ limit: 100 }),
    }).subscribe({
      next: ({ currencies, wallets, types, kinds, plans }) => {
        const directionByKind = new Map(
          kinds.filter((kind) => kind.code === 'Income' || kind.code === 'Expense').map((kind) => [kind.id, kind.code as 'Income' | 'Expense']),
        );
        this.currencies.set(currencies.data);
        this.wallets.set(wallets.data);
        this.typeOptions.set(
          types.data
            .filter((type) => directionByKind.has(type.behaviorKindId))
            .map((type) => ({ type, direction: directionByKind.get(type.behaviorKindId)! })),
        );
        this.allPlans.set(plans.data);
      },
      error: (error: unknown) => this.lookupsError.set(extractErrorMessage(error, 'Не удалось загрузить кошельки и справочники.')),
    });
  }

  // ---- occurrences ----

  previousMonth(): void {
    this.month.update((month) => shiftMonth(month, -1));
    this.loadOccurrences();
  }

  nextMonth(): void {
    this.month.update((month) => shiftMonth(month, 1));
    this.loadOccurrences();
  }

  currentMonth(): void {
    this.month.set(todayIsoDate().slice(0, 7));
    this.loadOccurrences();
  }

  isCurrentMonth(): boolean {
    return this.month() === todayIsoDate().slice(0, 7);
  }

  goToMonth(month: string): void {
    this.month.set(month);
    this.loadOccurrences();
  }

  loadOccurrences(): void {
    this.loadOverduePayments();
    const { from, to } = monthRange(this.month());
    const planId = this.occurrenceFilter.getRawValue().planId;
    this.occurrencesLoading.set(true);
    this.occurrencesError.set(null);
    this.planningService.listOccurrences({ from, to, plannedTransactionId: planId || undefined }).subscribe({
      next: (list) => {
        this.occurrences.set([...list.data].sort((a, b) => a.effectiveDate.localeCompare(b.effectiveDate)));
        this.occurrencesLoading.set(false);
      },
      error: (error: unknown) => {
        this.occurrencesError.set(extractErrorMessage(error, 'Не удалось загрузить наступления.'));
        this.occurrencesLoading.set(false);
      },
    });
  }

  private loadOverduePayments(): void {
    // days=1: only overdue items matter here; a failure (or no primary wallet) just hides the hint.
    this.reportingService.getUpcomingPayments(1).subscribe({
      next: (payments) =>
        this.overduePayments.set(payments.items.filter((item) => item.kind === 'PlannedExpense' && item.isOverdue)),
      error: () => this.overduePayments.set([]),
    });
  }

  resetOccurrenceFilter(): void {
    this.occurrenceFilter.setValue({ planId: '' });
  }

  openOccurrenceActions(occurrence: PlannedOccurrence): void {
    this.actionError.set(null);
    this.rowActionsOccurrence.set(occurrence);
  }

  closeOccurrenceActions(): void {
    this.nextOccurrenceAction = null;
    this.rowActionsOccurrence.set(null);
  }

  chooseOccurrenceAction(action: OccurrenceAction, occurrence: PlannedOccurrence): void {
    this.nextOccurrenceAction = { action, occurrence };
    this.rowActionsOccurrence.set(null);
  }

  onOccurrenceActionsClosed(): void {
    const next = this.nextOccurrenceAction;
    this.nextOccurrenceAction = null;
    if (!next) {
      return;
    }
    this.submitError.set(null);
    if (next.action === 'execute') {
      this.executeOccurrence.set(next.occurrence);
    } else if (next.action === 'move') {
      this.moveForm.reset({ date: next.occurrence.effectiveDate });
      this.moveOccurrence.set(next.occurrence);
    } else {
      this.amountForm.reset({ amount: next.occurrence.plannedAmount.amount });
      this.amountOccurrence.set(next.occurrence);
    }
  }

  /** Skip / unskip run right away from the actions dialog: both are reversible. */
  toggleSkip(occurrence: PlannedOccurrence): void {
    this.closeOccurrenceActions();
    const request$ =
      occurrence.status === 'Skipped'
        ? this.planningService.unskipOccurrence(occurrence.plannedTransactionId, occurrence.occurrenceDate)
        : this.planningService.skipOccurrence(occurrence.plannedTransactionId, occurrence.occurrenceDate);
    request$.subscribe({
      next: () => {
        this.successMessage.set(occurrence.status === 'Skipped' ? 'Пропуск отменён' : 'Наступление пропущено');
        this.loadOccurrences();
      },
      error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось изменить наступление.')),
    });
  }

  closeExecute(): void {
    if (!this.isSubmitting()) {
      this.executeOccurrence.set(null);
      this.submitError.set(null);
    }
  }

  saveExecute(request: PlannedOccurrenceExecuteRequest): void {
    const occurrence = this.executeOccurrence();
    if (!occurrence) {
      return;
    }
    this.submit(
      this.planningService.executeOccurrence(occurrence.plannedTransactionId, occurrence.occurrenceDate, request),
      'Не удалось исполнить наступление.',
      () => {
        this.executeOccurrence.set(null);
        this.successMessage.set('Операция создана');
        this.loadOccurrences();
        this.reloadPlansLookup();
      },
    );
  }

  closeMove(): void {
    if (!this.isSubmitting()) {
      this.moveOccurrence.set(null);
      this.submitError.set(null);
    }
  }

  saveMove(): void {
    const occurrence = this.moveOccurrence();
    if (!occurrence) {
      return;
    }
    if (this.moveForm.invalid) {
      this.moveForm.markAllAsTouched();
      return;
    }
    const date = this.moveForm.getRawValue().date;
    this.submit(this.planningService.moveOccurrence(occurrence.plannedTransactionId, occurrence.occurrenceDate, date), 'Не удалось перенести наступление.', () => {
      this.moveOccurrence.set(null);
      this.successMessage.set(date === occurrence.occurrenceDate ? 'Перенос отменён' : `Перенесено на ${formatDateText(date)}`);
      this.loadOccurrences();
    });
  }

  /** Moving back to the original schedule date cancels the move. */
  resetMove(): void {
    const occurrence = this.moveOccurrence();
    if (occurrence) {
      this.moveForm.setValue({ date: occurrence.occurrenceDate });
      this.saveMove();
    }
  }

  closeAmount(): void {
    if (!this.isSubmitting()) {
      this.amountOccurrence.set(null);
      this.submitError.set(null);
    }
  }

  saveAmount(reset = false): void {
    const occurrence = this.amountOccurrence();
    if (!occurrence) {
      return;
    }
    if (!reset && this.amountForm.invalid) {
      this.amountForm.markAllAsTouched();
      return;
    }
    const amount = reset ? null : this.amountForm.getRawValue().amount.trim();
    this.submit(
      this.planningService.changeOccurrenceAmount(occurrence.plannedTransactionId, occurrence.occurrenceDate, amount),
      'Не удалось изменить сумму наступления.',
      () => {
        this.amountOccurrence.set(null);
        this.successMessage.set(reset ? 'Сумма плана восстановлена' : 'Сумма наступления изменена');
        this.loadOccurrences();
      },
    );
  }

  moveDateError(): string | null {
    return fieldError(this.moveForm.controls.date, 'Укажите дату');
  }

  occurrenceAmountError(): string | null {
    return fieldError(this.amountForm.controls.amount, {
      required: 'Укажите сумму',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      positiveAmount: 'Сумма должна быть больше нуля',
    });
  }

  // ---- plans ----

  loadPlans(): void {
    this.plansLoading.set(true);
    this.plansError.set(null);
    const status = this.planFilter.getRawValue().status || undefined;
    this.planningService.list({ status }).subscribe({
      next: (page) => {
        this.plans.set(page.data);
        this.plansNextCursor.set(page.pagination.nextCursor);
        this.plansHasMore.set(page.pagination.hasMore);
        this.plansLoading.set(false);
      },
      error: (error: unknown) => {
        this.plansError.set(extractErrorMessage(error, 'Не удалось загрузить плановые операции.'));
        this.plansLoading.set(false);
      },
    });
  }

  loadMorePlans(): void {
    const cursor = this.plansNextCursor();
    if (!cursor || this.plansLoading()) {
      return;
    }
    this.plansLoading.set(true);
    const status = this.planFilter.getRawValue().status || undefined;
    this.planningService.list({ status, cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.plans(), page);
        this.plans.set(result.items);
        this.plansNextCursor.set(result.nextCursor);
        this.plansHasMore.set(result.hasMore);
        this.plansLoading.set(false);
      },
      error: (error: unknown) => {
        this.plansError.set(extractErrorMessage(error, 'Не удалось загрузить следующую страницу.'));
        this.plansLoading.set(false);
      },
    });
  }

  openCreatePlan(): void {
    this.submitError.set(null);
    this.planFormState.set({ plan: null });
  }

  closePlanForm(): void {
    if (!this.isSubmitting()) {
      this.planFormState.set(null);
      this.submitError.set(null);
    }
  }

  onDismissRequested(event: Event): void {
    if (this.isSubmitting()) {
      event.preventDefault();
    }
  }

  savePlan(request: PlannedTransactionWriteRequest): void {
    const state = this.planFormState();
    if (!state) {
      return;
    }
    const request$ = state.plan ? this.planningService.update(state.plan.id, request) : this.planningService.create(request);
    this.submit(request$, 'Не удалось сохранить плановую операцию.', () => {
      this.planFormState.set(null);
      this.successMessage.set(state.plan ? 'План сохранён' : 'План создан');
      this.loadPlans();
      this.loadOccurrences();
      this.reloadPlansLookup();
    });
  }

  openPlanActions(plan: PlannedTransaction): void {
    this.rowActionsPlan.set(plan);
  }

  closePlanActions(): void {
    this.nextPlanAction = null;
    this.rowActionsPlan.set(null);
  }

  choosePlanAction(action: PlanAction, plan: PlannedTransaction): void {
    this.nextPlanAction = { action, plan };
    this.rowActionsPlan.set(null);
  }

  onPlanActionsClosed(): void {
    const next = this.nextPlanAction;
    this.nextPlanAction = null;
    if (!next) {
      return;
    }
    this.submitError.set(null);
    switch (next.action) {
      case 'occurrences':
        this.activeTab.set('occurrences');
        this.occurrenceFilter.setValue({ planId: next.plan.id });
        break;
      case 'edit':
        this.planFormState.set({ plan: next.plan });
        break;
      case 'finish':
      case 'delete':
        this.confirmState.set({ action: next.action, plan: next.plan });
        break;
    }
  }

  closeConfirm(): void {
    if (!this.isSubmitting()) {
      this.confirmState.set(null);
      this.submitError.set(null);
    }
  }

  confirm(): void {
    const state = this.confirmState();
    if (!state) {
      return;
    }
    const request$: Observable<PlannedTransaction | null> =
      state.action === 'finish' ? this.planningService.finish(state.plan.id) : this.planningService.delete(state.plan.id);
    this.submit(request$, state.action === 'finish' ? 'Не удалось завершить план.' : 'Не удалось удалить план.', (result) => {
      this.confirmState.set(null);
      if (state.action === 'finish') {
        this.successMessage.set('План завершён');
      } else {
        // DELETE answers 200 with the plan when it had executions — the plan was finished instead (ADR-0010 §8).
        this.successMessage.set(result ? 'У плана есть исполнения — он завершён, а не удалён' : 'План удалён');
      }
      this.loadPlans();
      this.loadOccurrences();
      this.reloadPlansLookup();
    });
  }

  // ---- display helpers ----

  plan(id: string): PlannedTransaction | undefined {
    return this.allPlans().find((plan) => plan.id === id) ?? this.plans().find((plan) => plan.id === id);
  }

  isPlanActive(id: string): boolean {
    return this.plan(id)?.status !== 'Finished';
  }

  typeName(id: string): string {
    return this.typeOptions().find((option) => option.type.id === id)?.type.name ?? '—';
  }

  planTitle(plan: { description?: string | null; operationTypeId: string }): string {
    return plan.description?.trim() || this.typeName(plan.operationTypeId);
  }

  planOptionLabel(plan: PlannedTransaction): string {
    return `${this.planTitle(plan)} · ${this.money(plan.amount)}`;
  }

  currencyCode(id: string): string {
    return this.currencies().find((currency) => currency.id === id)?.code ?? '';
  }

  walletName(id: string | null | undefined): string {
    if (!id) {
      return '—';
    }
    return this.wallets().find((wallet) => wallet.id === id)?.name ?? 'Кошелёк недоступен';
  }

  money(value: Money): string {
    return formatMoney(value, value.currencyCode ?? this.currencyCode(value.currencyId));
  }

  signedMoney(occurrence: PlannedOccurrence, value: Money): string {
    return `${occurrence.direction === 'Income' ? '+' : '−'}${this.money(value)}`;
  }

  private reloadPlansLookup(): void {
    this.planningService.list({ limit: 100 }).subscribe({ next: (page) => this.allPlans.set(page.data), error: () => undefined });
  }

  private submit<T>(request$: Observable<T>, fallback: string, onSuccess: (result: T) => void): void {
    if (this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);
    request$.subscribe({
      next: (result) => {
        this.isSubmitting.set(false);
        onSuccess(result);
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(extractErrorMessage(error, fallback));
        if (error instanceof HttpErrorResponse && error.status === 409) {
          this.loadOccurrences();
        }
      },
    });
  }
}
