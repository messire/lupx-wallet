import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { Observable, forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { Debt, Loan } from '../../../../data-access/debts/debts-api.models';
import { DebtsApiService } from '../../../../data-access/debts/debts-api.service';
import { Fund, FundAllocationRequest, FundWriteRequest, WalletReservation } from '../../../../data-access/funds/funds-api.models';
import { FundsApiService } from '../../../../data-access/funds/funds-api.service';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { Money, formatMoney, isZeroAmount, ratioToPercent } from '@shared/money/money';
import { appendPage } from '@shared/pagination/cursor-page';
import { AllocationFormComponent, AllocationWalletOption } from '../../components/allocation-form/allocation-form.component';
import { FundFormComponent, FundLinkOption } from '../../components/fund-form/fund-form.component';
import { fundSaveErrorMessage } from '../../funds.models';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CheckboxComponent } from '@shared/ui/checkbox/checkbox.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { TabDefinition, TabsComponent } from '@shared/ui/tabs/tabs.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

type FundsTab = 'funds' | 'wallets';
type RowAction = 'reserve' | 'release' | 'details' | 'edit' | 'close' | 'delete';

/**
 * Funds (#7, ADR-0010 §4, #13): funds with target, reserve, progress and the reserve-coverage deficit
 * («дефицит обеспечения резерва», В7); create/edit/close/delete; an optional link to a debt or a loan (В9);
 * reserve/release per wallet of the fund currency showing the wallet's free funds; and the per-wallet overview
 * (balance, reserved, free, deficit). A reserve never changes a wallet balance.
 */
@Component({
  selector: 'app-funds',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    AllocationFormComponent,
    FundFormComponent,
    BadgeComponent,
    ButtonComponent,
    CheckboxComponent,
    DialogComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    LoadingStateComponent,
    SkeletonComponent,
    TabsComponent,
    ToastComponent,
  ],
  templateUrl: './funds-page.component.html',
  styleUrl: './funds-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FundsComponent implements OnInit {
  private readonly fundsService = inject(FundsApiService);
  private readonly debtsService = inject(DebtsApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  readonly tabs: TabDefinition[] = [
    { id: 'funds', label: 'Фонды' },
    { id: 'wallets', label: 'Резервы по кошелькам' },
  ];
  readonly activeTab = signal<FundsTab>('funds');

  readonly currencies = signal<Currency[]>([]);
  readonly wallets = signal<Wallet[]>([]);
  readonly debts = signal<Debt[]>([]);
  readonly loans = signal<Loan[]>([]);
  readonly reservations = signal<WalletReservation[]>([]);
  readonly reservationsError = signal<string | null>(null);
  readonly lookupsError = signal<string | null>(null);

  readonly funds = signal<Fund[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly filterForm = this.formBuilder.nonNullable.group({ includeClosed: [false] });

  readonly successMessage = signal<string | null>(null);

  readonly formState = signal<{ fund: Fund | null } | null>(null);
  readonly allocationState = signal<{ mode: 'reserve' | 'release'; fund: Fund } | null>(null);
  readonly detailsFund = signal<Fund | null>(null);
  readonly confirmState = signal<{ action: 'close' | 'delete'; fund: Fund } | null>(null);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly rowActionsFund = signal<Fund | null>(null);
  private nextRowAction: { action: RowAction; fund: Fund } | null = null;

  /** Active debts/loans to link with, plus the currently linked one of the edited fund. */
  readonly linkOptions = computed<FundLinkOption[]>(() => {
    const fund = this.formState()?.fund;
    const debts = this.debts()
      .filter((debt) => debt.status === 'Active' || debt.id === fund?.linkedDebtId)
      .map((debt) => ({ value: `debt:${debt.id}`, label: `Долг: ${debt.name}` }));
    const loans = this.loans()
      .filter((loan) => loan.status === 'Active' || loan.id === fund?.linkedLoanId)
      .map((loan) => ({ value: `loan:${loan.id}`, label: `Кредит: ${loan.name}` }));
    return [...debts, ...loans];
  });

  readonly allocationWallets = computed<AllocationWalletOption[]>(() => {
    const state = this.allocationState();
    if (!state) {
      return [];
    }
    if (state.mode === 'release') {
      return state.fund.allocations.map((allocation) => ({
        id: allocation.walletId,
        name: this.walletName(allocation.walletId),
        available: allocation.amount.amount,
      }));
    }
    const free = new Map(this.reservations().map((item) => [item.walletId, item.free.amount]));
    return this.wallets()
      .filter((wallet) => !wallet.isArchived && wallet.currencyId === state.fund.currencyId)
      .map((wallet) => ({ id: wallet.id, name: wallet.name, available: free.get(wallet.id) ?? wallet.currentBalance.amount }));
  });

  /** Non-archived wallets, plus archived ones that still show a reserve (never expected, but not hidden if it happens). */
  readonly visibleReservations = computed(() =>
    this.reservations().filter((item) => {
      const wallet = this.wallets().find((candidate) => candidate.id === item.walletId);
      return !wallet?.isArchived || !isZeroAmount(item.reserved.amount);
    }),
  );

  constructor() {
    this.filterForm.controls.includeClosed.valueChanges.pipe(takeUntilDestroyed()).subscribe(() => this.loadFunds());
  }

  ngOnInit(): void {
    this.loadLookups();
    this.loadFunds();
    this.loadReservations();
  }

  selectTab(id: string): void {
    this.activeTab.set(id as FundsTab);
  }

  loadLookups(): void {
    this.lookupsError.set(null);
    forkJoin({
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      wallets: this.walletsService.list({ includeArchived: true, limit: 100 }),
      debts: this.debtsService.listDebts({ limit: 100 }),
      loans: this.debtsService.listLoans({ limit: 100 }),
    }).subscribe({
      next: ({ currencies, wallets, debts, loans }) => {
        this.currencies.set(currencies.data);
        this.wallets.set(wallets.data);
        this.debts.set(debts.data);
        this.loans.set(loans.data);
      },
      error: (error: unknown) => this.lookupsError.set(extractErrorMessage(error, 'Не удалось загрузить кошельки, валюты и долги.')),
    });
  }

  loadFunds(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.fundsService.list({ includeClosed: this.filterForm.getRawValue().includeClosed }).subscribe({
      next: (page) => {
        this.funds.set(page.data);
        this.nextCursor.set(page.pagination.nextCursor);
        this.hasMore.set(page.pagination.hasMore);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить фонды.'));
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
    this.fundsService.list({ includeClosed: this.filterForm.getRawValue().includeClosed, cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.funds(), page);
        this.funds.set(result.items);
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

  loadReservations(): void {
    this.reservationsError.set(null);
    this.fundsService.listWalletReservations().subscribe({
      next: (items) => this.reservations.set(items),
      error: (error: unknown) => this.reservationsError.set(extractErrorMessage(error, 'Не удалось загрузить резервы по кошелькам.')),
    });
  }

  openCreateForm(): void {
    this.submitError.set(null);
    this.formState.set({ fund: null });
  }

  closeForm(): void {
    if (!this.isSubmitting()) {
      this.formState.set(null);
      this.submitError.set(null);
    }
  }

  onDismissRequested(event: Event): void {
    if (this.isSubmitting()) {
      event.preventDefault();
    }
  }

  saveFund(request: FundWriteRequest): void {
    const state = this.formState();
    if (!state) {
      return;
    }
    const request$ = state.fund ? this.fundsService.update(state.fund.id, request) : this.fundsService.create(request);
    this.submit(
      request$,
      'Не удалось сохранить фонд.',
      () => {
        this.formState.set(null);
        this.successMessage.set(state.fund ? 'Фонд сохранён' : 'Фонд создан');
      },
      (error) => {
        const mapped = fundSaveErrorMessage(error, request);
        if (mapped.reloadLinks) {
          this.loadLookups();
        }
        return mapped.message;
      },
    );
  }

  openRowActions(fund: Fund): void {
    this.rowActionsFund.set(fund);
  }

  closeRowActions(): void {
    this.nextRowAction = null;
    this.rowActionsFund.set(null);
  }

  chooseRowAction(action: RowAction, fund: Fund): void {
    this.nextRowAction = { action, fund };
    this.rowActionsFund.set(null);
  }

  onRowActionsClosed(): void {
    const next = this.nextRowAction;
    this.nextRowAction = null;
    if (!next) {
      return;
    }
    this.submitError.set(null);
    switch (next.action) {
      case 'reserve':
      case 'release':
        this.loadReservations();
        this.allocationState.set({ mode: next.action, fund: next.fund });
        break;
      case 'details':
        this.detailsFund.set(next.fund);
        break;
      case 'edit':
        this.formState.set({ fund: next.fund });
        break;
      case 'close':
      case 'delete':
        this.confirmState.set({ action: next.action, fund: next.fund });
        break;
    }
  }

  closeAllocation(): void {
    if (!this.isSubmitting()) {
      this.allocationState.set(null);
      this.submitError.set(null);
    }
  }

  saveAllocation(request: FundAllocationRequest): void {
    const state = this.allocationState();
    if (!state) {
      return;
    }
    const request$ = state.mode === 'reserve' ? this.fundsService.reserve(state.fund.id, request) : this.fundsService.release(state.fund.id, request);
    this.submit(request$, state.mode === 'reserve' ? 'Не удалось зарезервировать.' : 'Не удалось снять резерв.', () => {
      this.allocationState.set(null);
      this.successMessage.set(state.mode === 'reserve' ? 'Деньги зарезервированы' : 'Резерв снят');
    });
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
    const request$: Observable<unknown> = state.action === 'close' ? this.fundsService.close(state.fund.id) : this.fundsService.delete(state.fund.id);
    this.submit(request$, state.action === 'close' ? 'Не удалось закрыть фонд.' : 'Не удалось удалить фонд: по нему уже были резервы, его можно только закрыть.', () => {
      this.confirmState.set(null);
      this.successMessage.set(state.action === 'close' ? 'Фонд закрыт, резервы сняты' : 'Фонд удалён');
    });
  }

  // ---- display helpers ----

  percent(fund: Fund): string {
    return ratioToPercent(fund.progress);
  }

  /** Width of the progress bar fill, capped at 100%. */
  progressWidth(fund: Fund): string {
    const value = ratioToPercent(fund.progress);
    return `${value.length > 3 || Number(value) > 100 ? 100 : Number(value)}%`;
  }

  hasDeficit(money: Money): boolean {
    return !isZeroAmount(money.amount);
  }

  linkLabel(fund: Fund): string | null {
    if (fund.linkedDebtId) {
      return `Долг: ${this.debts().find((debt) => debt.id === fund.linkedDebtId)?.name ?? 'недоступен'}`;
    }
    if (fund.linkedLoanId) {
      return `Кредит: ${this.loans().find((loan) => loan.id === fund.linkedLoanId)?.name ?? 'недоступен'}`;
    }
    return null;
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

  private submit(
    request$: Observable<unknown>,
    fallback: string,
    onSuccess: () => void,
    mapError: (error: unknown) => string = (error) => extractErrorMessage(error, fallback),
  ): void {
    if (this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);
    request$.subscribe({
      next: () => {
        this.isSubmitting.set(false);
        onSuccess();
        this.loadFunds();
        this.loadReservations();
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(mapError(error));
        if (error instanceof HttpErrorResponse && error.status === 409) {
          // Free funds may have changed since the form opened (an expense, another fund) — refresh the hints.
          this.loadReservations();
        }
      },
    });
  }
}
