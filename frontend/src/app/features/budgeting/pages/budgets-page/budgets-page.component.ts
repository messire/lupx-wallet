import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { Observable, forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { Budget, BudgetCategoryExecution, BudgetExecution, ExpenseCategory } from '../../../../data-access/budgeting/budgeting-api.models';
import { BudgetingApiService } from '../../../../data-access/budgeting/budgeting-api.service';
import { Currency, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { formatAmount } from '@shared/money/money';
import { formatMonthLabel, shiftMonth, todayIsoDate } from '@shared/ui/date-input/calendar-date';
import { CategoryFormComponent, CategoryFormValue } from '../../components/category-form/category-form.component';
import { LimitFormComponent, LimitFormValue } from '../../components/limit-form/limit-form.component';
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
import { TabDefinition, TabsComponent } from '@shared/ui/tabs/tabs.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

type BudgetsTab = 'budget' | 'categories';

/**
 * Monthly budgets and expense categories (#11, ADR-0010 §5, #13). The budget tab: a month switcher, the budget's
 * currency (one budget per month and currency), category limits and the execution table — limit, actual, remaining
 * or overspent, the «Без бюджета» row, totals, and a note on applied historical rates and expenses excluded for a
 * missing rate (В8). The categories tab edits the dictionary that groups expense operation types (В1).
 */
@Component({
  selector: 'app-budgets',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    CategoryFormComponent,
    LimitFormComponent,
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
    TabsComponent,
    ToastComponent,
  ],
  templateUrl: './budgets-page.component.html',
  styleUrl: './budgets-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BudgetsComponent implements OnInit {
  private readonly budgetingService = inject(BudgetingApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly formatAmount = formatAmount;
  protected readonly formatMonthLabel = formatMonthLabel;

  readonly tabs: TabDefinition[] = [
    { id: 'budget', label: 'Бюджет месяца' },
    { id: 'categories', label: 'Категории расходов' },
  ];
  readonly activeTab = signal<BudgetsTab>('budget');

  readonly currencies = signal<Currency[]>([]);
  /** Expense operation types, inactive ones included (category membership may keep a deactivated type). */
  readonly expenseTypes = signal<OperationType[]>([]);
  readonly categories = signal<ExpenseCategory[]>([]);
  readonly lookupsError = signal<string | null>(null);

  readonly month = signal(todayIsoDate().slice(0, 7));
  readonly budgets = signal<Budget[]>([]);
  readonly selectedBudgetId = signal<string | null>(null);
  /** Switches between the month's budgets in different currencies. */
  readonly budgetSwitch = new FormControl('', { nonNullable: true });
  readonly execution = signal<BudgetExecution | null>(null);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly executionError = signal<string | null>(null);

  readonly successMessage = signal<string | null>(null);
  readonly actionError = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly createBudgetOpen = signal(false);
  readonly createBudgetForm = this.formBuilder.nonNullable.group({ currencyId: ['', Validators.required] });
  readonly limitState = signal<{ categoryId: string | null; limit: string } | null>(null);
  readonly confirmState = signal<
    { action: 'remove-limit'; row: BudgetCategoryExecution } | { action: 'delete-budget' } | { action: 'delete-category'; category: ExpenseCategory } | null
  >(null);
  readonly categoryFormState = signal<{ category: ExpenseCategory | null } | null>(null);

  readonly selectedBudget = computed(() => this.budgets().find((budget) => budget.id === this.selectedBudgetId()) ?? null);
  readonly budgetCurrencyCode = computed(() => this.currencyCode(this.selectedBudget()?.currencyId ?? ''));

  /** Currencies without a budget in the selected month — offered when creating one. */
  readonly creatableCurrencies = computed(() => {
    const used = new Set(this.budgets().map((budget) => budget.currencyId));
    return this.currencies().filter((currency) => !used.has(currency.id));
  });

  /** Active categories that have no limit in the selected budget — offered by «Добавить лимит». */
  readonly categoriesWithoutLimit = computed(() => {
    const limited = new Set(this.selectedBudget()?.limits.map((limit) => limit.expenseCategoryId) ?? []);
    return this.categories().filter((category) => category.isActive && !limited.has(category.id));
  });

  readonly limitFormCategories = computed(() => {
    const state = this.limitState();
    if (state?.categoryId) {
      const row = this.execution()?.categories.find((item) => item.expenseCategoryId === state.categoryId);
      const category = this.categories().find((item) => item.id === state.categoryId);
      return category ? [category] : row ? [{ id: row.expenseCategoryId, name: row.name, isActive: row.isActive, operationTypeIds: [] }] : [];
    }
    return this.categoriesWithoutLimit();
  });

  /** Active expense types that are not in any category — their expenses always land in «Без бюджета». */
  readonly uncategorizedTypes = computed(() => {
    const assigned = new Set(this.categories().flatMap((category) => category.operationTypeIds));
    return this.expenseTypes().filter((type) => type.isActive && !assigned.has(type.id));
  });

  constructor() {
    this.budgetSwitch.valueChanges.pipe(takeUntilDestroyed()).subscribe((id) => {
      if (id && id !== this.selectedBudgetId()) {
        this.selectBudget(id);
      }
    });
  }

  ngOnInit(): void {
    this.loadLookups();
    this.loadBudgets();
  }

  selectTab(id: string): void {
    this.activeTab.set(id as BudgetsTab);
  }

  loadLookups(): void {
    this.lookupsError.set(null);
    forkJoin({
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      types: this.referenceDataService.listOperationTypes({ includeInactive: true, limit: 100 }),
      kinds: this.referenceDataService.listOperationBehaviorKinds(),
      categories: this.budgetingService.listCategories({ includeInactive: true, limit: 100 }),
    }).subscribe({
      next: ({ currencies, types, kinds, categories }) => {
        const expenseKindIds = new Set(kinds.filter((kind) => kind.code === 'Expense').map((kind) => kind.id));
        this.currencies.set(currencies.data);
        this.expenseTypes.set(types.data.filter((type) => expenseKindIds.has(type.behaviorKindId)));
        this.categories.set(categories.data);
      },
      error: (error: unknown) => this.lookupsError.set(extractErrorMessage(error, 'Не удалось загрузить категории и справочники.')),
    });
  }

  previousMonth(): void {
    this.month.update((month) => shiftMonth(month, -1));
    this.loadBudgets();
  }

  nextMonth(): void {
    this.month.update((month) => shiftMonth(month, 1));
    this.loadBudgets();
  }

  currentMonth(): void {
    this.month.set(todayIsoDate().slice(0, 7));
    this.loadBudgets();
  }

  isCurrentMonth(): boolean {
    return this.month() === todayIsoDate().slice(0, 7);
  }

  /** Loads the month's budgets; keeps the selected currency when the new month has a budget in it. */
  loadBudgets(preferredBudgetId?: string): void {
    const previousCurrency = this.selectedBudget()?.currencyId;
    const requestedMonth = this.month();
    this.isLoading.set(true);
    this.loadError.set(null);
    this.execution.set(null);
    this.budgetingService.listBudgets({ month: requestedMonth, limit: 100 }).subscribe({
      next: (page) => {
        // A response for a month the user has already navigated away from is stale.
        if (requestedMonth !== this.month()) {
          return;
        }
        this.budgets.set(page.data);
        const next =
          page.data.find((budget) => budget.id === preferredBudgetId) ??
          page.data.find((budget) => budget.currencyId === previousCurrency) ??
          page.data[0] ??
          null;
        this.selectedBudgetId.set(next?.id ?? null);
        this.budgetSwitch.setValue(next?.id ?? '', { emitEvent: false });
        this.isLoading.set(false);
        if (next) {
          this.loadExecution();
        }
      },
      error: (error: unknown) => {
        if (requestedMonth !== this.month()) {
          return;
        }
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить бюджеты.'));
        this.isLoading.set(false);
      },
    });
  }

  selectBudget(id: string): void {
    this.selectedBudgetId.set(id);
    this.loadExecution();
  }

  loadExecution(): void {
    const budgetId = this.selectedBudgetId();
    if (!budgetId) {
      return;
    }
    this.executionError.set(null);
    this.budgetingService.getExecution(budgetId).subscribe({
      next: (execution) => {
        if (execution.budgetId === this.selectedBudgetId()) {
          this.execution.set(execution);
        }
      },
      error: (error: unknown) => this.executionError.set(extractErrorMessage(error, 'Не удалось рассчитать исполнение бюджета.')),
    });
  }

  // ---- budget create / delete ----

  openCreateBudget(): void {
    this.submitError.set(null);
    this.createBudgetForm.reset({ currencyId: this.creatableCurrencies().length === 1 ? this.creatableCurrencies()[0].id : '' });
    this.createBudgetOpen.set(true);
  }

  closeCreateBudget(): void {
    if (!this.isSubmitting()) {
      this.createBudgetOpen.set(false);
    }
  }

  onDismissRequested(event: Event): void {
    if (this.isSubmitting()) {
      event.preventDefault();
    }
  }

  submitCreateBudget(): void {
    if (this.createBudgetForm.invalid) {
      this.createBudgetForm.markAllAsTouched();
      return;
    }
    const request = { month: this.month(), currencyId: this.createBudgetForm.getRawValue().currencyId };
    this.submit(this.budgetingService.createBudget(request), 'Не удалось создать бюджет.', (budget) => {
      this.createBudgetOpen.set(false);
      this.successMessage.set('Бюджет создан. Задайте лимиты по категориям');
      this.loadBudgets((budget as Budget).id);
    });
  }

  // ---- limits ----

  openAddLimit(): void {
    this.submitError.set(null);
    this.limitState.set({ categoryId: null, limit: '' });
  }

  openEditLimit(row: BudgetCategoryExecution): void {
    this.submitError.set(null);
    this.limitState.set({ categoryId: row.expenseCategoryId, limit: row.limit });
  }

  closeLimit(): void {
    if (!this.isSubmitting()) {
      this.limitState.set(null);
      this.submitError.set(null);
    }
  }

  saveLimit(value: LimitFormValue): void {
    const budget = this.selectedBudget();
    if (!budget) {
      return;
    }
    this.submit(this.budgetingService.setLimit(budget.id, value.categoryId, value.limit), 'Не удалось сохранить лимит.', (updated) => {
      this.limitState.set(null);
      this.replaceBudget(updated as Budget);
      this.successMessage.set('Лимит сохранён');
      this.loadExecution();
    });
  }

  requestRemoveLimit(row: BudgetCategoryExecution): void {
    this.submitError.set(null);
    this.confirmState.set({ action: 'remove-limit', row });
  }

  requestDeleteBudget(): void {
    this.submitError.set(null);
    this.confirmState.set({ action: 'delete-budget' });
  }

  closeConfirm(): void {
    if (!this.isSubmitting()) {
      this.confirmState.set(null);
      this.submitError.set(null);
    }
  }

  confirm(): void {
    const state = this.confirmState();
    const budget = this.selectedBudget();
    if (!state) {
      return;
    }
    if (state.action === 'remove-limit' && budget) {
      this.submit(this.budgetingService.removeLimit(budget.id, state.row.expenseCategoryId), 'Не удалось убрать лимит.', (updated) => {
        this.confirmState.set(null);
        this.replaceBudget(updated as Budget);
        this.successMessage.set('Лимит убран');
        this.loadExecution();
      });
    } else if (state.action === 'delete-budget' && budget) {
      this.submit(this.budgetingService.deleteBudget(budget.id), 'Не удалось удалить бюджет.', () => {
        this.confirmState.set(null);
        this.selectedBudgetId.set(null);
        this.successMessage.set('Бюджет удалён');
        this.loadBudgets();
      });
    } else if (state.action === 'delete-category') {
      this.submit(
        this.budgetingService.deleteCategory(state.category.id),
        'Не удалось удалить категорию: она используется в лимите бюджета. Её можно деактивировать.',
        () => {
          this.confirmState.set(null);
          this.categories.update((list) => list.filter((item) => item.id !== state.category.id));
          this.successMessage.set('Категория удалена');
        },
      );
    }
  }

  // ---- categories ----

  openCategoryForm(category: ExpenseCategory | null): void {
    this.submitError.set(null);
    this.categoryFormState.set({ category });
  }

  closeCategoryForm(): void {
    if (!this.isSubmitting()) {
      this.categoryFormState.set(null);
      this.submitError.set(null);
    }
  }

  saveCategory(value: CategoryFormValue): void {
    const state = this.categoryFormState();
    if (!state) {
      return;
    }
    const request$ = state.category
      ? this.budgetingService.updateCategory(state.category.id, value)
      : this.budgetingService.createCategory(value);
    this.submit(request$, 'Не удалось сохранить категорию.', (saved) => {
      const category = saved as ExpenseCategory;
      this.categoryFormState.set(null);
      this.categories.update((list) =>
        state.category ? list.map((item) => (item.id === category.id ? category : item)) : [...list, category],
      );
      this.successMessage.set(state.category ? 'Категория сохранена' : 'Категория добавлена');
      this.loadExecution();
    });
  }

  deactivateCategory(category: ExpenseCategory): void {
    this.actionError.set(null);
    this.budgetingService.deactivateCategory(category.id).subscribe({
      next: (updated) => {
        this.categories.update((list) => list.map((item) => (item.id === updated.id ? updated : item)));
        this.successMessage.set('Категория деактивирована');
      },
      error: (error: unknown) => this.actionError.set(extractErrorMessage(error, 'Не удалось деактивировать категорию.')),
    });
  }

  requestDeleteCategory(category: ExpenseCategory): void {
    this.submitError.set(null);
    this.confirmState.set({ action: 'delete-category', category });
  }

  // ---- display helpers ----

  currencyCode(id: string): string {
    return this.currencies().find((currency) => currency.id === id)?.code ?? '';
  }

  amount(value: string, currencyId?: string): string {
    const code = currencyId ? this.currencyCode(currencyId) : this.budgetCurrencyCode();
    return `${formatAmount(value)} ${code}`.trim();
  }

  /** «Осталось 120.00» or «Перерасход 30.00» — the sign is spelled out, not only colored. */
  remainingLabel(remaining: string): string {
    return remaining.startsWith('-') ? `Перерасход ${this.amount(remaining.slice(1))}` : this.amount(remaining);
  }

  typeNames(category: ExpenseCategory): string {
    const names = category.operationTypeIds.map((id) => this.expenseTypes().find((type) => type.id === id)?.name ?? '—');
    return names.length ? names.join(', ') : 'нет типов';
  }

  typeNameList(types: OperationType[]): string {
    return types.map((type) => type.name).join(', ');
  }

  private replaceBudget(updated: Budget): void {
    this.budgets.update((list) => list.map((budget) => (budget.id === updated.id ? updated : budget)));
  }

  private submit(request$: Observable<unknown>, fallback: string, onSuccess: (result: unknown) => void): void {
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
      },
    });
  }
}
