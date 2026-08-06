import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { extractErrorMessage } from '../../core/http/problem-details';
import { formatMoney } from '../../core/money/money';
import { Currency } from '../reference-data/reference-data.models';
import { ReferenceDataService } from '../reference-data/reference-data.service';
import { Wallet } from '../wallets/wallets.models';
import { WalletsService } from '../wallets/wallets.service';
import { TotalAmount } from './reporting.models';
import { ReportingService } from './reporting.service';

/**
 * UC-19 (текущая общая сумма) и UC-21 (историческая сумма на дату), docs/PROGRESS.md W3.1.
 * Названия кошельков/коды валют подтягиваются через уже существующие
 * WalletsService/ReferenceDataService (без дублирования HTTP-вызовов), тем же приёмом,
 * что используется в features/balance-history и features/exchange-rates.
 */
@Component({
  selector: 'app-reporting',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './reporting.component.html',
  styleUrl: './reporting.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportingComponent implements OnInit {
  private readonly reportingService = inject(ReportingService);
  private readonly walletsService = inject(WalletsService);
  private readonly referenceDataService = inject(ReferenceDataService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly formatMoney = formatMoney;

  private readonly wallets = signal<Wallet[]>([]);
  private readonly currencies = signal<Currency[]>([]);

  // UC-19 — текущая сумма, загружается сразу при открытии экрана.
  readonly currentTotal = signal<TotalAmount | null>(null);
  readonly currentLoading = signal(false);
  readonly currentError = signal<string | null>(null);
  /** Сумма не может быть посчитана — в системе нет основного кошелька (GET возвращает 404). */
  readonly currentNoPrimaryWallet = signal(false);

  // UC-21 — историческая сумма на выбранную дату.
  readonly dateForm = this.formBuilder.nonNullable.group({ date: [''] });
  readonly historicalTotal = signal<TotalAmount | null>(null);
  readonly historicalLoading = signal(false);
  readonly historicalError = signal<string | null>(null);
  readonly historicalNoPrimaryWallet = signal(false);

  ngOnInit(): void {
    this.loadLookups();
    this.loadCurrent();
  }

  loadCurrent(): void {
    this.currentLoading.set(true);
    this.currentError.set(null);
    this.currentNoPrimaryWallet.set(false);

    this.reportingService.getTotalAmount().subscribe({
      next: (total) => {
        this.currentTotal.set(total);
        this.currentLoading.set(false);
      },
      error: (error: unknown) => {
        this.currentTotal.set(null);
        this.currentLoading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.currentNoPrimaryWallet.set(true);
          return;
        }
        this.currentError.set(extractErrorMessage(error, 'Не удалось загрузить текущую сумму.'));
      },
    });
  }

  showHistorical(): void {
    const date = this.dateForm.getRawValue().date;
    if (!date || this.historicalLoading()) {
      return;
    }

    this.historicalLoading.set(true);
    this.historicalError.set(null);
    this.historicalNoPrimaryWallet.set(false);

    this.reportingService.getTotalAmount(date).subscribe({
      next: (total) => {
        this.historicalTotal.set(total);
        this.historicalLoading.set(false);
      },
      error: (error: unknown) => {
        this.historicalTotal.set(null);
        this.historicalLoading.set(false);
        if (error instanceof HttpErrorResponse && error.status === 404) {
          this.historicalNoPrimaryWallet.set(true);
          return;
        }
        this.historicalError.set(extractErrorMessage(error, 'Не удалось загрузить сумму на дату.'));
      },
    });
  }

  currencyCode(currencyId: string): string {
    return this.currencies().find((currency) => currency.id === currencyId)?.code ?? '';
  }

  walletName(walletId: string): string {
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? walletId;
  }

  /**
   * "Курс по состоянию на ..." — показывается только когда фактическая дата курса
   * (`ratesAsOfDate`) отличается от запрошенной даты (для текущей суммы запрошенная
   * дата — сегодня, `total.date` в этом случае null).
   */
  ratesNote(total: TotalAmount): string | null {
    const requestedDate = total.date ?? this.today();
    return total.ratesAsOfDate !== requestedDate ? total.ratesAsOfDate : null;
  }

  private loadLookups(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
    this.referenceDataService
      .listCurrencies({ includeInactive: true, limit: 100 })
      .subscribe((page) => this.currencies.set(page.data));
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
