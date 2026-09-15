import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { EMPTY, Subject, catchError, map, of, switchMap } from 'rxjs';
import { appendPage } from '@shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '@shared/money/money';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { BalanceSnapshot, BalanceSnapshotPage } from '../../../../data-access/balance-history/balance-history-api.models';
import { BalanceHistoryApiService } from '../../../../data-access/balance-history/balance-history-api.service';
import { CardComponent } from '@shared/ui/card/card.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';

/** A `null` request cancels whatever inner request is currently in flight for that scenario (see `switchMap` below) without issuing a new HTTP call — used on wallet change. */
type BalanceDateRequest = { walletId: string; date?: string } | null;

interface BalanceDateResult {
  snapshot: BalanceSnapshot | null;
  error: string | null;
}

type HistoryMode = 'replace' | 'append';

type HistoryRequest = { walletId: string; from: string; to: string; cursor?: string; mode: HistoryMode } | null;

interface HistoryResult {
  mode: HistoryMode;
  page: BalanceSnapshotPage | null;
  error: string | null;
}

/**
 * UC-18 (wallet balance as of a date), UC-20 (balance history for a period),
 * UC-23 (cascading history recalculation on retroactive edits — a background
 * backend mechanism, ADR-0003; this component just displays the already
 * recalculated result, no separate "recalculated" indicator is needed).
 *
 * Race protection (plan B6/B7): both scenarios route their requests through a
 * `Subject` + `switchMap`. `switchMap` unsubscribes the previous in-flight
 * inner observable as soon as a new value arrives on the subject, so a late
 * response from a superseded request can never reach the final `subscribe`
 * and overwrite a more recent result. Selecting a different wallet pushes a
 * `null` request, which `switchMap` turns into `EMPTY` — this cancels any
 * in-flight request for that scenario without making a new HTTP call.
 */
@Component({
  selector: 'app-balance-history',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    CardComponent,
    FieldComponent,
    FieldControlDirective,
    ButtonComponent,
    BadgeComponent,
    EmptyStateComponent,
    LoadingStateComponent,
    SkeletonComponent,
    ErrorStateComponent,
  ],
  templateUrl: './balance-history-page.component.html',
  styleUrl: './balance-history-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BalanceHistoryComponent implements OnInit {
  private readonly balanceHistoryService = inject(BalanceHistoryApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);
  private readonly destroyRef = inject(DestroyRef);

  protected readonly formatMoney = formatMoney;

  readonly wallets = signal<Wallet[]>([]);
  readonly currencies = signal<Currency[]>([]);

  readonly walletForm = this.formBuilder.nonNullable.group({ walletId: [''] });
  private readonly selectedWalletId = signal('');

  readonly selectedWallet = computed<Wallet | null>(
    () => this.wallets().find((wallet) => wallet.id === this.selectedWalletId()) ?? null,
  );

  readonly balanceDateForm = this.formBuilder.nonNullable.group({ date: [''] });
  readonly balanceSnapshot = signal<BalanceSnapshot | null>(null);
  readonly balanceLoading = signal(false);
  readonly balanceError = signal<string | null>(null);
  private readonly balanceDateTrigger$ = new Subject<BalanceDateRequest>();
  /** Parameters of the last dispatched balance-on-date request (successful or failed) — used by `retryBalanceOnDate()` so a retry replays what was actually sent, not the current (possibly-since-edited) form value. */
  private lastBalanceDateRequest: { walletId: string; date?: string } | null = null;

  readonly rangeForm = this.formBuilder.nonNullable.group({ from: [''], to: [''] });
  readonly rangeValidationError = signal<string | null>(null);
  readonly historyLoadError = signal<string | null>(null);
  readonly historyLoading = signal(false);
  readonly history = signal<BalanceSnapshot[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);
  private readonly historyTrigger$ = new Subject<HistoryRequest>();
  /** Parameters of the last dispatched history request (successful or failed, replace or append) — used by `retryHistory()` so a retry replays what was actually sent, not the current (possibly-since-edited) form value. */
  private lastHistoryRequest: { walletId: string; from: string; to: string; cursor?: string; mode: HistoryMode } | null = null;

  /**
   * Snapshot of the period actually sent to the server (plan B5), captured at
   * request time — not read back from the form, so editing the fields after a
   * response does not change the caption already shown for that response.
   */
  readonly appliedRangeFrom = signal<string | null>(null);
  readonly appliedRangeTo = signal<string | null>(null);

  readonly historyCountLabel = computed<string | null>(() => {
    if (this.appliedRangeFrom() === null) {
      return null;
    }
    if (this.historyLoading() || this.historyLoadError()) {
      return '—';
    }
    return `Показано: ${this.history().length}`;
  });

  ngOnInit(): void {
    this.loadWallets();
    this.loadCurrencies();

    this.walletForm.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((walletId) => {
      this.selectedWalletId.set(walletId);
      this.resetResults();
    });

    this.balanceDateTrigger$
      .pipe(
        switchMap((request): ReturnType<typeof this.runBalanceDateRequest> => {
          if (!request) {
            return EMPTY;
          }
          return this.runBalanceDateRequest(request);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe(({ snapshot, error }) => {
        this.balanceSnapshot.set(snapshot);
        this.balanceError.set(error);
        this.balanceLoading.set(false);
      });

    this.historyTrigger$
      .pipe(
        switchMap((request): ReturnType<typeof this.runHistoryRequest> => {
          if (!request) {
            return EMPTY;
          }
          return this.runHistoryRequest(request);
        }),
        takeUntilDestroyed(this.destroyRef),
      )
      .subscribe((result) => this.applyHistoryResult(result));
  }

  currencyCode(currencyId: string): string {
    return this.currencies().find((currency) => currency.id === currencyId)?.code ?? '';
  }

  /** Q13: balance before the wallet's accounting start date is treated as 0 — used to highlight such rows. */
  isBeforeAccountingStart(date: string): boolean {
    const wallet = this.selectedWallet();
    return !!wallet && date < wallet.accountingStartDate;
  }

  showBalanceOnDate(): void {
    const walletId = this.selectedWalletId();
    if (!walletId || this.balanceLoading()) {
      return;
    }

    this.balanceLoading.set(true);
    this.balanceError.set(null);

    const date = this.balanceDateForm.getRawValue().date || undefined;
    this.lastBalanceDateRequest = { walletId, date };
    this.balanceDateTrigger$.next(this.lastBalanceDateRequest);
  }

  /** Retries exactly the last dispatched balance-on-date request, ignoring any form edits made since it was sent (plan B5 follow-up). */
  retryBalanceOnDate(): void {
    const request = this.lastBalanceDateRequest;
    if (!request || this.balanceLoading()) {
      return;
    }

    this.balanceLoading.set(true);
    this.balanceError.set(null);
    this.balanceDateTrigger$.next(request);
  }

  showHistory(): void {
    const walletId = this.selectedWalletId();
    if (!walletId || this.historyLoading()) {
      return;
    }

    const { from, to } = this.rangeForm.getRawValue();
    if (!from || !to) {
      this.rangeValidationError.set('Укажите обе даты диапазона.');
      return;
    }
    // Client-side validation of from > to — not sent to the server.
    if (from > to) {
      this.rangeValidationError.set('Дата начала диапазона не может быть позже даты окончания.');
      return;
    }

    this.rangeValidationError.set(null);
    this.historyLoadError.set(null);
    this.historyLoading.set(true);
    this.appliedRangeFrom.set(from);
    this.appliedRangeTo.set(to);

    this.lastHistoryRequest = { walletId, from, to, mode: 'replace' };
    this.historyTrigger$.next(this.lastHistoryRequest);
  }

  loadMoreHistory(): void {
    const walletId = this.selectedWalletId();
    const cursor = this.nextCursor();
    const from = this.appliedRangeFrom();
    const to = this.appliedRangeTo();
    if (!walletId || !cursor || !from || !to || this.historyLoading()) {
      return;
    }

    // Clear a stale error from a previous "Load more" attempt, otherwise it
    // stays visible on top of the new attempt's own skeleton/result (MINOR-4).
    this.historyLoadError.set(null);
    this.historyLoading.set(true);
    this.lastHistoryRequest = { walletId, from, to, cursor, mode: 'append' };
    this.historyTrigger$.next(this.lastHistoryRequest);
  }

  /** Retries exactly the last dispatched history request (replace or append), ignoring any form edits made since it was sent (plan B5 follow-up). */
  retryHistory(): void {
    const request = this.lastHistoryRequest;
    if (!request || this.historyLoading()) {
      return;
    }

    this.historyLoadError.set(null);
    this.historyLoading.set(true);
    this.historyTrigger$.next(request);
  }

  private runBalanceDateRequest(request: { walletId: string; date?: string }) {
    return this.balanceHistoryService.getBalanceOnDate(request.walletId, request.date).pipe(
      map((snapshot): BalanceDateResult => ({ snapshot, error: null })),
      catchError((error: unknown) =>
        of<BalanceDateResult>({ snapshot: null, error: extractErrorMessage(error, 'Не удалось получить баланс на дату.') }),
      ),
    );
  }

  private runHistoryRequest(request: { walletId: string; from: string; to: string; cursor?: string; mode: HistoryMode }) {
    return this.balanceHistoryService.listHistory(request.walletId, request.from, request.to, { cursor: request.cursor }).pipe(
      map((page): HistoryResult => ({ mode: request.mode, page, error: null })),
      catchError((error: unknown) =>
        of<HistoryResult>({
          mode: request.mode,
          page: null,
          error: extractErrorMessage(
            error,
            request.mode === 'append' ? 'Не удалось загрузить следующую страницу.' : 'Не удалось загрузить историю баланса.',
          ),
        }),
      ),
    );
  }

  private applyHistoryResult(result: HistoryResult): void {
    this.historyLoading.set(false);

    if (result.error || !result.page) {
      if (result.mode === 'replace') {
        this.history.set([]);
        this.nextCursor.set(null);
        this.hasMore.set(false);
      }
      this.historyLoadError.set(result.error);
      return;
    }

    this.historyLoadError.set(null);

    if (result.mode === 'replace') {
      this.history.set(result.page.data);
      this.nextCursor.set(result.page.pagination.nextCursor);
      this.hasMore.set(result.page.pagination.hasMore);
    } else {
      const appended = appendPage(this.history(), result.page);
      this.history.set(appended.items);
      this.nextCursor.set(appended.nextCursor);
      this.hasMore.set(appended.hasMore);
    }
  }

  private loadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
  }

  private loadCurrencies(): void {
    this.referenceDataService.listCurrencies({ limit: 100 }).subscribe((page) => this.currencies.set(page.data));
  }

  private resetResults(): void {
    // Cancel any in-flight request for both scenarios before clearing state —
    // otherwise a response for the previous wallet could still land afterwards
    // and repopulate these signals with data for the wallet no longer selected.
    this.balanceDateTrigger$.next(null);
    this.historyTrigger$.next(null);

    this.balanceSnapshot.set(null);
    this.balanceLoading.set(false);
    this.balanceError.set(null);
    this.balanceDateForm.reset({ date: '' });
    this.lastBalanceDateRequest = null;

    this.rangeForm.reset({ from: '', to: '' });
    this.rangeValidationError.set(null);
    this.historyLoadError.set(null);
    this.historyLoading.set(false);
    this.history.set([]);
    this.nextCursor.set(null);
    this.hasMore.set(false);
    this.appliedRangeFrom.set(null);
    this.appliedRangeTo.set(null);
    this.lastHistoryRequest = null;
  }
}
