import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { PlannedTransaction } from '../../../../data-access/planning/planning-api.models';
import { PlanningApiService } from '../../../../data-access/planning/planning-api.service';
import { Currency, OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { AppliedRate, CashFlowForecast, ForecastHorizon, ForecastMovement } from '../../../../data-access/reporting/reporting-api.models';
import { ReportingApiService } from '../../../../data-access/reporting/reporting-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { Money, formatMoney } from '@shared/money/money';
import { formatDateText } from '@shared/ui/date-input/calendar-date';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ForecastChartComponent } from '../../components/forecast-chart/forecast-chart.component';
import { FORECAST_HORIZONS, MOVEMENT_KIND_LABELS, isNegativeAmount, rateLabel } from '../../reporting.models';

/**
 * Cash-flow forecast (#10, ADR-0010 §7): expected balance in the primary wallet currency for 1/3/6/12 months,
 * computed by the server from actual balances, expected planned occurrences and unpaid loan schedule items.
 */
@Component({
  selector: 'app-forecast',
  standalone: true,
  imports: [RouterLink, BadgeComponent, EmptyStateComponent, ErrorStateComponent, LoadingStateComponent, SkeletonComponent, ForecastChartComponent],
  templateUrl: './forecast-page.component.html',
  styleUrl: './forecast-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ForecastComponent implements OnInit {
  private readonly reportingService = inject(ReportingApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly planningService = inject(PlanningApiService);

  protected readonly horizons = FORECAST_HORIZONS;
  protected readonly kindLabels = MOVEMENT_KIND_LABELS;
  protected readonly formatDate = formatDateText;
  protected readonly isNegative = isNegativeAmount;

  private readonly wallets = signal<Wallet[]>([]);
  private readonly currencies = signal<Currency[]>([]);
  private readonly operationTypes = signal<OperationType[]>([]);
  private readonly plans = signal<PlannedTransaction[]>([]);

  readonly horizon = signal<ForecastHorizon>(3);
  readonly forecast = signal<CashFlowForecast | null>(null);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly noPrimaryWallet = signal(false);

  readonly calculationCurrency = computed(() => {
    const forecast = this.forecast();
    return forecast ? this.currencyCode(forecast.startBalance) : '';
  });

  /** Days with movements (the first entry is today, which may have none). */
  readonly movementDays = computed(() => this.forecast()?.days.filter((day) => day.movements.length > 0) ?? []);

  ngOnInit(): void {
    this.loadLookups();
    this.load();
  }

  selectHorizon(horizon: ForecastHorizon): void {
    if (horizon === this.horizon()) {
      return;
    }
    this.horizon.set(horizon);
    this.load();
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.noPrimaryWallet.set(false);
    const horizon = this.horizon();
    this.reportingService.getCashFlowForecast(horizon).subscribe({
      next: (forecast) => {
        // A slower answer for a previous horizon must not overwrite the current one.
        if (horizon !== this.horizon()) {
          return;
        }
        this.forecast.set(forecast);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        if (horizon !== this.horizon()) {
          return;
        }
        this.forecast.set(null);
        this.isLoading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.noPrimaryWallet.set(true);
          return;
        }
        this.loadError.set(extractErrorMessage(error, 'Не удалось построить прогноз.'));
      },
    });
  }

  money(value: Money): string {
    return formatMoney(value, this.currencyCode(value));
  }

  currencyCode(value: Money | string): string {
    if (typeof value !== 'string' && value.currencyCode) {
      return value.currencyCode;
    }
    const id = typeof value === 'string' ? value : value.currencyId;
    return this.currencies().find((currency) => currency.id === id)?.code ?? '';
  }

  rate(rate: AppliedRate): string {
    return rateLabel(rate, (id) => this.currencyCode(id));
  }

  walletName(walletId: string | null | undefined): string {
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? 'Кошелёк';
  }

  movementTitle(movement: ForecastMovement): string {
    if (movement.kind === 'LoanPayment') {
      return movement.loanName || this.kindLabels.LoanPayment;
    }
    const plan = this.plans().find((candidate) => candidate.id === movement.plannedTransactionId);
    const typeName = plan ? this.operationTypes().find((type) => type.id === plan.operationTypeId)?.name : undefined;
    return plan?.description?.trim() || typeName || this.kindLabels[movement.kind];
  }

  /** Converted amount differs from the item one only for a foreign currency. */
  isConverted(movement: ForecastMovement): boolean {
    return movement.amount.currencyId !== movement.convertedAmount.currencyId;
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
      // Names fall back to generic labels; the forecast amounts are still correct.
      error: () => undefined,
    });
  }
}
