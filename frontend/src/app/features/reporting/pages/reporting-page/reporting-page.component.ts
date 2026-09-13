import { HttpErrorResponse } from '@angular/common/http';
import { ChangeDetectionStrategy, Component, OnInit, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '@shared/money/money';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { TotalAmount } from '../../../../data-access/reporting/reporting-api.models';
import { ReportingApiService } from '../../../../data-access/reporting/reporting-api.service';
import { CardComponent } from '@shared/ui/card/card.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';

/**
 * UC-19 (current total) and UC-21 (historical total for a date). Wallet names and
 * currency codes are fetched via the existing WalletsApiService/ReferenceDataApiService
 * (no duplicated HTTP calls), the same approach used in features/balance-history and
 * features/exchange-rates.
 */
@Component({
  selector: 'app-reporting',
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
  templateUrl: './reporting-page.component.html',
  styleUrl: './reporting-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportingComponent implements OnInit {
  private readonly reportingService = inject(ReportingApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly formatMoney = formatMoney;

  private readonly wallets = signal<Wallet[]>([]);
  private readonly currencies = signal<Currency[]>([]);

  // UC-19 — current total, loaded immediately when the screen opens.
  readonly currentTotal = signal<TotalAmount | null>(null);
  readonly currentLoading = signal(false);
  readonly currentError = signal<string | null>(null);
  /** The total cannot be computed — there is no primary wallet in the system (GET returns 404). */
  readonly currentNoPrimaryWallet = signal(false);

  // UC-21 — historical total for the selected date.
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
   * "Rate as of ..." note — shown only when the actual rate date (`ratesAsOfDate`)
   * differs from the requested date (for the current total, the requested date is
   * today, and `total.date` is null in that case).
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
