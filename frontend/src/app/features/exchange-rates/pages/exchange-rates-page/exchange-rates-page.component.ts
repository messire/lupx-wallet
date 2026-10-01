import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { forkJoin } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatAmount } from '@shared/money/money';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { ExchangeRateQuote, LatestExchangeRates } from '../../../../data-access/exchange-rates/exchange-rates-api.models';
import { ExchangeRatesApiService } from '../../../../data-access/exchange-rates/exchange-rates-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { CardComponent } from '@shared/ui/card/card.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';
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
 * via ReferenceDataApiService (/currencies — active currencies only), the same
 * service used by the reference-data screen (features/reference-data), so the HTTP
 * call is not duplicated.
 *
 * A manual refresh also reloads the list of available currencies (ADR-0004 §7), so
 * the button stays usable without a primary wallet — in that case only that list is
 * refreshed and no rates are shown. `GET /exchange-rates/latest` reads the database
 * only and never calls the rates source (ADR-0004 §8).
 *
 * The primary currency (ADR-0004 §5: quotes are computed relative to the current
 * primary wallet's currency) is resolved from GET /wallets (`isPrimary`) → its
 * `currencyId` → the code via the already-loaded /currencies reference data —
 * not guessed from a row's `toCurrencyId`, which would be unavailable while the
 * rates list is empty.
 */
@Component({
  selector: 'app-exchange-rates',
  standalone: true,
  imports: [
    CardComponent,
    ButtonComponent,
    BadgeComponent,
    ToastComponent,
    EmptyStateComponent,
    LoadingStateComponent,
    SkeletonComponent,
    ErrorStateComponent,
  ],
  changeDetection: ChangeDetectionStrategy.OnPush,
  templateUrl: './exchange-rates-page.component.html',
  styleUrl: './exchange-rates-page.component.scss',
})
export class ExchangeRatesComponent implements OnInit {
  private readonly exchangeRatesService = inject(ExchangeRatesApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly walletsService = inject(WalletsApiService);

  private readonly rates = signal<ExchangeRateQuote[]>([]);
  private readonly currencies = signal<Currency[]>([]);
  private readonly wallets = signal<Wallet[]>([]);
  readonly lastSuccessfulUpdate = signal<string | null>(null);

  /** Initial load only — a refresh in progress never re-shows the skeleton (C5). */
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly isRefreshing = signal(false);
  readonly successMessage = signal<string | null>(null);

  /**
   * UC-09, failure branch: Frankfurter unavailable (502) while previous rates
   * exist — an expected, non-critical state (ADR-0004 §6: the previous rates
   * are kept). `GET /exchange-rates/latest` never reports this on its own — the
   * status is only known as the result of a manual refresh in this session
   * — so this is only ever set from `refresh()`, never from `loadAll()`.
   */
  readonly sourceUnavailable = signal<string | null>(null);

  /**
   * UC-09, failure branch: the source is unavailable AND there is no previous
   * data to fall back to at all — a distinct empty state (no quotes are
   * fabricated), not the generic `loadError` full-page error.
   */
  readonly sourceUnavailableEmpty = signal<string | null>(null);

  /**
   * UC-09, failure branch: `refresh()` failed with anything other than the
   * expected 502 (e.g. 500, 504, a dropped connection → status 0). Already
   * loaded rows must stay on screen (ADR-0004 §6) — this is a banner over the
   * table, never routed through `loadError`, which drives the full-page
   * `app-error-state` that would otherwise wipe them out. `loadError` is
   * reserved for the primary `loadAll()` failure only.
   */
  readonly refreshError = signal<string | null>(null);

  readonly primaryWallet = computed<Wallet | null>(() => this.wallets().find((wallet) => wallet.isPrimary) ?? null);

  readonly primaryCurrencyCode = computed<string | null>(() => {
    const wallet = this.primaryWallet();
    return wallet ? this.currencyCode(wallet.currencyId) : null;
  });

  /** True once wallets have loaded and none of them is primary — should not
   *  happen (exactly one primary wallet always exists, see ddd-model.md), but
   *  handled as a polite empty state rather than a raw error/blank screen. */
  readonly primaryWalletMissing = computed(() => !this.isLoading() && !this.loadError() && this.primaryWallet() === null);

  /**
   * Each row uses the quote's own `toCurrencyId` (MAJOR-4) — never
   * `primaryCurrencyCode()` (ADR-0004 §5: recompute on primary wallet change
   * runs in the background) so a row that has not been recomputed yet after a
   * primary wallet switch still shows the currency it is actually quoted
   * against, not a mislabelled one. `primaryCurrencyCode()` is only a fallback
   * for the section heading/direction phrase, not for row values.
   */
  readonly rows = computed<ExchangeRateRow[]>(() =>
    this.rates().map((quote) => ({
      fromCurrencyCode: this.currencyCode(quote.fromCurrencyId),
      toCurrencyCode: this.currencyCode(quote.toCurrencyId),
      rate: formatAmount(quote.rate),
    })),
  );

  readonly lastUpdateLabel = computed(() => {
    if (this.isLoading()) {
      return '—';
    }
    const value = this.lastSuccessfulUpdate();
    return value ? new Date(value).toLocaleString('ru-RU') : 'Ещё не было';
  });

  /**
   * Empty rates list without a source failure. "Ещё не было" is only true when no update has ever
   * succeeded; once there is a `lastSuccessfulUpdate` an empty list means only the primary currency is
   * active (rates are quoted for the other active currencies), which needs a different explanation.
   */
  readonly emptyRates = computed(() =>
    this.lastSuccessfulUpdate() === null
      ? {
          title: 'Ещё не было',
          description: 'Нажмите «Обновить сейчас», чтобы получить актуальные курсы и список доступных валют.',
        }
      : {
          title: 'Нет курсов: активна только основная валюта',
          description:
            'Курсы загружаются для активных валют, отличных от основной. Добавьте валюту на экране «Справочники» (вкладка «Валюты») или создайте кошелёк в другой валюте.',
        },
  );

  ngOnInit(): void {
    this.loadAll();
  }

  loadAll(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.sourceUnavailable.set(null);
    this.sourceUnavailableEmpty.set(null);

    forkJoin({
      latest: this.exchangeRatesService.getLatest(),
      currencies: this.referenceDataService.listCurrencies({ limit: 100 }),
      wallets: this.walletsService.list({ includeArchived: true, limit: 100 }),
    }).subscribe({
      next: ({ latest, currencies, wallets }) => {
        this.applyLatest(latest);
        this.currencies.set(currencies.data);
        this.wallets.set(wallets.data);
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
    this.sourceUnavailable.set(null);
    this.sourceUnavailableEmpty.set(null);
    this.refreshError.set(null);

    this.exchangeRatesService.refresh().subscribe({
      next: (latest) => {
        this.applyLatest(latest);
        this.isRefreshing.set(false);
        // Without a primary wallet only the list of available currencies is refreshed
        // (openapi.yaml: /exchange-rates/refresh), so the confirmation says exactly that.
        this.successMessage.set(
          this.primaryWallet() === null ? 'Список доступных валют обновлён.' : 'Курсы валют обновлены.',
        );
      },
      error: (error: unknown) => {
        this.isRefreshing.set(false);

        if (error instanceof HttpErrorResponse && error.status === 502) {
          // The 502 body is the same LatestExchangeRates schema with the previous
          // rates (openapi.yaml: /exchange-rates/refresh), not application/problem+json.
          // Guarded (not just cast) because a 502 can also come from infrastructure
          // in front of the app (e.g. a reverse-proxy gateway timeout) with a
          // non-JSON body — that must not be misread as "previous data present".
          const body = this.asLatestExchangeRates(error.error);
          if (body) {
            this.applyLatest(body);
          }

          if (this.primaryWalletMissing()) {
            // Without a primary wallet only the list of available currencies is refreshed — there are no
            // rates on screen, so "last known rates are shown" would be false.
            this.sourceUnavailableEmpty.set(extractErrorMessage(error, 'Не удалось обновить список доступных валют.'));
          } else if (this.lastSuccessfulUpdate() !== null) {
            this.sourceUnavailable.set(
              'Курсы или список доступных валют обновить не удалось. Показаны последние известные курсы.',
            );
          } else {
            // No previous data at all (not even before this attempt) — do not
            // fabricate rows, show a dedicated empty state with the error and a retry.
            this.sourceUnavailableEmpty.set(
              extractErrorMessage(error, 'Источник курсов недоступен, а прежних данных ещё нет.'),
            );
          }
          return;
        }

        // Any other refresh failure (500, 504, a dropped connection → status 0):
        // this must never go through `loadError` — that drives the full-page
        // `app-error-state` (see template) which would replace the table and
        // wipe out rows already on screen (MAJOR-3, ADR-0004 §6). `loadError`
        // stays reserved for the primary `loadAll()` failure only.
        this.refreshError.set(extractErrorMessage(error, 'Не удалось обновить курсы валют.'));
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

  private asLatestExchangeRates(value: unknown): LatestExchangeRates | null {
    if (!value || typeof value !== 'object' || !Array.isArray((value as Partial<LatestExchangeRates>).rates)) {
      return null;
    }
    return value as LatestExchangeRates;
  }
}
