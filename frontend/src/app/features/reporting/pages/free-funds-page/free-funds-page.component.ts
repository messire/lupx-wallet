import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { PlannedTransaction } from '../../../../data-access/planning/planning-api.models';
import { PlanningApiService } from '../../../../data-access/planning/planning-api.service';
import { Currency, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { FreeFunds, UpcomingPayment, UpcomingPayments } from '../../../../data-access/reporting/reporting-api.models';
import { ReportingApiService } from '../../../../data-access/reporting/reporting-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { Money, formatMoney, isZeroAmount } from '@shared/money/money';
import { formatDateText } from '@shared/ui/date-input/calendar-date';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { UPCOMING_DAY_OPTIONS, UPCOMING_KIND_LABELS, isNegativeAmount, rateLabel } from '../../reporting.models';

/**
 * Free funds (#12, ADR-0010 §7, В9): actual / reserved / free / deficit in the primary wallet currency, per-wallet
 * breakdown, and the upcoming payments as a separate list — they are NOT subtracted from free funds.
 */
@Component({
  selector: 'app-free-funds',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    BadgeComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    FieldComponent,
    LoadingStateComponent,
    SelectComponent,
    SelectOptionDirective,
    SkeletonComponent,
  ],
  templateUrl: './free-funds-page.component.html',
  styleUrl: './free-funds-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FreeFundsComponent implements OnInit {
  private readonly reportingService = inject(ReportingApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly planningService = inject(PlanningApiService);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly formatDate = formatDateText;
  protected readonly kindLabels = UPCOMING_KIND_LABELS;
  protected readonly dayOptions = UPCOMING_DAY_OPTIONS;
  protected readonly isNegative = isNegativeAmount;
  protected readonly isZero = isZeroAmount;

  private readonly wallets = signal<Wallet[]>([]);
  private readonly currencies = signal<Currency[]>([]);
  private readonly operationTypes = signal<OperationType[]>([]);
  private readonly plans = signal<PlannedTransaction[]>([]);

  readonly freeFunds = signal<FreeFunds | null>(null);
  readonly freeFundsLoading = signal(false);
  readonly freeFundsError = signal<string | null>(null);
  readonly noPrimaryWallet = signal(false);

  readonly daysControl = new FormControl('30', { nonNullable: true });
  readonly upcoming = signal<UpcomingPayments | null>(null);
  readonly upcomingLoading = signal(false);
  readonly upcomingError = signal<string | null>(null);

  readonly hasDeficit = computed(() => {
    const funds = this.freeFunds();
    return !!funds && !isZeroAmount(funds.deficitAmount.amount);
  });

  readonly overdueCount = computed(() => this.upcoming()?.items.filter((item) => item.isOverdue).length ?? 0);

  ngOnInit(): void {
    this.loadLookups();
    this.loadFreeFunds();
    this.loadUpcoming();
    this.daysControl.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe(() => this.loadUpcoming());
  }

  loadFreeFunds(): void {
    this.freeFundsLoading.set(true);
    this.freeFundsError.set(null);
    this.noPrimaryWallet.set(false);
    this.reportingService.getFreeFunds().subscribe({
      next: (funds) => {
        this.freeFunds.set(funds);
        this.freeFundsLoading.set(false);
      },
      error: (error: unknown) => {
        this.freeFunds.set(null);
        this.freeFundsLoading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.noPrimaryWallet.set(true);
          return;
        }
        this.freeFundsError.set(extractErrorMessage(error, 'Не удалось загрузить свободные средства.'));
      },
    });
  }

  loadUpcoming(): void {
    this.upcomingLoading.set(true);
    this.upcomingError.set(null);
    this.reportingService.getUpcomingPayments(Number(this.daysControl.value)).subscribe({
      next: (payments) => {
        this.upcoming.set(payments);
        this.upcomingLoading.set(false);
      },
      error: (error: unknown) => {
        this.upcoming.set(null);
        this.upcomingLoading.set(false);
        // Without a primary wallet the free-funds block already explains it.
        if (error instanceof HttpErrorResponse && error.status === 404) {
          return;
        }
        this.upcomingError.set(extractErrorMessage(error, 'Не удалось загрузить ближайшие платежи.'));
      },
    });
  }

  money(value: Money | null | undefined): string {
    return value ? formatMoney(value, value.currencyCode ?? this.currencyCode(value.currencyId)) : '—';
  }

  currencyCode(currencyId: string): string {
    return this.currencies().find((currency) => currency.id === currencyId)?.code ?? '';
  }

  walletName(walletId: string | null | undefined): string {
    if (!walletId) {
      return '';
    }
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? 'Кошелёк';
  }

  rate(rate: FreeFunds['rates'][number]): string {
    return rateLabel(rate, (id) => this.currencyCode(id));
  }

  /** «Аренда» (plan description or operation type) or «Ипотека · Сбербанк» for a loan payment. */
  paymentTitle(item: UpcomingPayment): string {
    if (item.kind === 'LoanPayment') {
      return [item.loanName, item.creditorName].filter(Boolean).join(' · ') || this.kindLabels.LoanPayment;
    }
    const plan = this.plans().find((candidate) => candidate.id === item.plannedTransactionId);
    const typeName = this.operationTypes().find((type) => type.id === item.operationTypeId)?.name;
    return plan?.description?.trim() || typeName || this.kindLabels.PlannedExpense;
  }

  paymentLink(item: UpcomingPayment): string {
    return item.kind === 'LoanPayment' ? '/loans' : '/planning';
  }

  movedFrom(item: UpcomingPayment): string | null {
    return item.kind === 'PlannedExpense' && item.occurrenceDate && item.occurrenceDate !== item.date ? item.occurrenceDate : null;
  }

  private loadLookups(): void {
    forkJoin({
      wallets: this.walletsService.list({ includeArchived: true, limit: 100 }),
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      types: this.referenceDataService.listOperationTypes({ includeInactive: true, limit: 100 }),
      plans: this.planningService.list({ limit: 100 }),
    }).subscribe({
      next: ({ wallets, currencies, types, plans }) => {
        this.wallets.set(wallets.data);
        this.currencies.set(currencies.data);
        this.operationTypes.set(types.data);
        this.plans.set(plans.data);
      },
      // Names fall back to generic labels; the amounts are still correct.
      error: () => undefined,
    });
  }
}
