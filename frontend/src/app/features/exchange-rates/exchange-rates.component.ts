import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../core/http/problem-details';
import { formatAmount } from '../../core/money/money';
import { Currency } from '../reference-data/reference-data.models';
import { ReferenceDataService } from '../reference-data/reference-data.service';
import { ExchangeRateQuote, LatestExchangeRates } from './exchange-rates.models';
import { ExchangeRatesService } from './exchange-rates.service';

interface ExchangeRateRow {
  fromCurrencyCode: string;
  toCurrencyCode: string;
  rate: string;
}

/**
 * UC-08 (автообновление — виден результат последнего прогона), UC-09 (кнопка ручного
 * обновления), UC-10 (метка времени последнего успешного обновления). docs/PROGRESS.md,
 * W2.4. Коды валют — через ReferenceDataService (/currencies), переиспользуется тот же
 * сервис, что и на экране справочников (features/reference-data), HTTP-вызов не
 * дублируется.
 */
@Component({
  selector: 'app-exchange-rates',
  standalone: true,
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exchange-rates.component.html',
  styleUrl: './exchange-rates.component.scss',
})
export class ExchangeRatesComponent implements OnInit {
  private readonly exchangeRatesService = inject(ExchangeRatesService);
  private readonly referenceDataService = inject(ReferenceDataService);

  private readonly rates = signal<ExchangeRateQuote[]>([]);
  private readonly currencies = signal<Currency[]>([]);
  readonly lastSuccessfulUpdate = signal<string | null>(null);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly isRefreshing = signal(false);

  /**
   * UC-09, отказная ветка: Frankfurter недоступен (502) — это ожидаемое, не аварийное
   * состояние (ADR-0001 п.6: прежние курсы сохраняются). Отдельно от `loadError`,
   * чтобы не выглядеть как поломка приложения.
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
          // Тело 502 — та же схема LatestExchangeRates с прежними курсами
          // (openapi.yaml: /exchange-rates/refresh), не application/problem+json.
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
