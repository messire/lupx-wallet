import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatAmount } from '@shared/money/money';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { ExchangeRateQuote, LatestExchangeRates } from '../../../../data-access/exchange-rates/exchange-rates-api.models';
import { ExchangeRatesApiService } from '../../../../data-access/exchange-rates/exchange-rates-api.service';
import { CardComponent } from '@shared/ui/card/card.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';

interface ExchangeRateRow {
  fromCurrencyCode: string;
  toCurrencyCode: string;
  rate: string;
}

/**
 * UC-08 (auto-refresh — shows the result of the last run), UC-09 (manual refresh
 * button), UC-10 (timestamp of the last successful update). Currency codes come
 * via ReferenceDataApiService (/currencies), the same service used by the
 * reference-data screen (features/reference-data), so the HTTP call is not duplicated.
 */
@Component({
  selector: 'app-exchange-rates',
  standalone: true,
  imports: [CardComponent, ButtonComponent, EmptyStateComponent, LoadingStateComponent, SkeletonComponent, ErrorStateComponent],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exchange-rates-page.component.html',
  styleUrl: './exchange-rates-page.component.scss',
})
export class ExchangeRatesComponent implements OnInit {
  private readonly exchangeRatesService = inject(ExchangeRatesApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);

  private readonly rates = signal<ExchangeRateQuote[]>([]);
  private readonly currencies = signal<Currency[]>([]);
  readonly lastSuccessfulUpdate = signal<string | null>(null);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly isRefreshing = signal(false);

  /**
   * UC-09, failure branch: Frankfurter unavailable (502) — this is an expected,
   * non-critical state (ADR-0001 §6: the previous rates are kept). Kept separate
   * from `loadError` so it does not look like an application failure.
   */
  readonly sourceUnavailable = signal<string | null>(null);

  readonly rows = computed<ExchangeRateRow[]>(() =>
    this.rates().map((quote) => ({
      fromCurrencyCode: this.currencyCode(quote.fromCurrencyId),
      toCurrencyCode: this.currencyCode(quote.toCurrencyId),
      rate: formatAmount(quote.rate),
    })),
  );

  readonly lastUpdateLabel = computed(() => {
    const value = this.lastSuccessfulUpdate();
    return value ? new Date(value).toLocaleString('ru-RU') : 'никогда';
  });

  ngOnInit(): void {
    this.loadAll();
  }

  loadAll(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.sourceUnavailable.set(null);

    forkJoin({
      latest: this.exchangeRatesService.getLatest(),
      currencies: this.referenceDataService.listCurrencies({ includeInactive: true, limit: 100 }),
    }).subscribe({
      next: ({ latest, currencies }) => {
        this.applyLatest(latest);
        this.currencies.set(currencies.data);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить курсы валют.'));
        this.isLoading.set(false);
      },
    });
  }

  refresh(): void {
    if (this.isRefreshing()) {
      return;
    }

    this.isRefreshing.set(true);
    this.loadError.set(null);
    this.sourceUnavailable.set(null);

    this.exchangeRatesService.refresh().subscribe({
      next: (latest) => {
        this.applyLatest(latest);
        this.isRefreshing.set(false);
      },
      error: (error: unknown) => {
        this.isRefreshing.set(false);

        if (error instanceof HttpErrorResponse && error.status === 502) {
          // The 502 body is the same LatestExchangeRates schema with the previous
          // rates (openapi.yaml: /exchange-rates/refresh), not application/problem+json.
          const body = error.error as LatestExchangeRates | null;
          if (body) {
            this.applyLatest(body);
          }
          this.sourceUnavailable.set('Источник курсов недоступен, показаны последние известные курсы.');
          return;
        }

        this.loadError.set(extractErrorMessage(error, 'Не удалось обновить курсы валют.'));
      },
    });
  }

  private applyLatest(latest: LatestExchangeRates): void {
    this.rates.set(latest.rates);
    this.lastSuccessfulUpdate.set(latest.lastSuccessfulUpdate);
  }

  private currencyCode(currencyId: string): string {
    return this.currencies().find((currency) => currency.id === currencyId)?.code ?? currencyId;
  }
}
