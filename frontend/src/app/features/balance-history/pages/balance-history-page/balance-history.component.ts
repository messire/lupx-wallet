import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { ChangeDetectionStrategy, Component, DestroyRef, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { appendPage } from '../../../../shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '../../../../shared/money/money';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { BalanceSnapshot } from '../../../../data-access/balance-history/balance-history-api.models';
import { BalanceHistoryApiService } from '../../../../data-access/balance-history/balance-history-api.service';

/**
 * UC-18 (баланс кошелька на дату), UC-20 (история баланса за период),
 * UC-23 (каскадный пересчёт истории при ретроактивных правках — фоновая
 * механика бэкенда, ADR-0008; здесь просто отображается уже пересчитанный
 * результат — отдельного индикатора "пересчитано" не требуется).
 */
@Component({
  selector: 'app-balance-history',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './balance-history.component.html',
  styleUrl: './balance-history.component.scss',
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

  readonly rangeForm = this.formBuilder.nonNullable.group({ from: [''], to: [''] });
  readonly rangeValidationError = signal<string | null>(null);
  readonly historyLoadError = signal<string | null>(null);
  readonly historyLoading = signal(false);
  readonly history = signal<BalanceSnapshot[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);

  ngOnInit(): void {
    this.loadWallets();
    this.loadCurrencies();

    this.walletForm.controls.walletId.valueChanges.pipe(takeUntilDestroyed(this.destroyRef)).subscribe((walletId) => {
      this.selectedWalletId.set(walletId);
      this.resetResults();
    });
  }

  currencyCode(currencyId: string): string {
    return this.currencies().find((currency) => currency.id === currencyId)?.code ?? '';
  }

  /** Q13: баланс до даты начала учёта кошелька считается равным 0 — используется, чтобы явно подсветить такие строки. */
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
    this.balanceHistoryService.getBalanceOnDate(walletId, date).subscribe({
      next: (snapshot) => {
        this.balanceSnapshot.set(snapshot);
        this.balanceLoading.set(false);
      },
      error: (error: unknown) => {
        this.balanceSnapshot.set(null);
        this.balanceError.set(extractErrorMessage(error, 'Не удалось получить баланс на дату.'));
        this.balanceLoading.set(false);
      },
    });
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
    // Клиентская валидация from > to — не отправляется на сервер (docs/PROGRESS.md, W2.3 DoD).
    if (from > to) {
      this.rangeValidationError.set('Дата начала диапазона не может быть позже даты окончания.');
      return;
    }

    this.rangeValidationError.set(null);
    this.historyLoadError.set(null);
    this.historyLoading.set(true);

    this.balanceHistoryService.listHistory(walletId, from, to).subscribe({
      next: (page) => {
        this.history.set(page.data);
        this.nextCursor.set(page.pagination.nextCursor);
        this.hasMore.set(page.pagination.hasMore);
        this.historyLoading.set(false);
      },
      error: (error: unknown) => {
        this.history.set([]);
        this.nextCursor.set(null);
        this.hasMore.set(false);
        this.historyLoadError.set(extractErrorMessage(error, 'Не удалось загрузить историю баланса.'));
        this.historyLoading.set(false);
      },
    });
  }

  loadMoreHistory(): void {
    const walletId = this.selectedWalletId();
    const cursor = this.nextCursor();
    if (!walletId || !cursor || this.historyLoading()) {
      return;
    }

    const { from, to } = this.rangeForm.getRawValue();
    this.historyLoading.set(true);

    this.balanceHistoryService.listHistory(walletId, from, to, { cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.history(), page);
        this.history.set(result.items);
        this.nextCursor.set(result.nextCursor);
        this.hasMore.set(result.hasMore);
        this.historyLoading.set(false);
      },
      error: (error: unknown) => {
        this.historyLoadError.set(extractErrorMessage(error, 'Не удалось загрузить следующую страницу.'));
        this.historyLoading.set(false);
      },
    });
  }

  private loadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
  }

  private loadCurrencies(): void {
    this.referenceDataService.listCurrencies({ limit: 100 }).subscribe((page) => this.currencies.set(page.data));
  }

  private resetResults(): void {
    this.balanceSnapshot.set(null);
    this.balanceError.set(null);
    this.balanceDateForm.reset({ date: '' });
    this.rangeForm.reset({ from: '', to: '' });
    this.rangeValidationError.set(null);
    this.historyLoadError.set(null);
    this.history.set([]);
    this.nextCursor.set(null);
    this.hasMore.set(false);
  }
}
