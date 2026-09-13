import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { appendPage } from '@shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '@shared/money/money';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { CreateTransferRequest, Transfer } from '../../../../data-access/transfers/transfers-api.models';
import { TransfersApiService } from '../../../../data-access/transfers/transfers-api.service';
import { TransferFormComponent, TransferWalletOption } from '../../components/transfer-form/transfer-form.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { EmptyStateComponent } from '@shared/ui/empty-state/empty-state.component';
import { ErrorStateComponent } from '@shared/ui/error-state/error-state.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { LoadingStateComponent } from '@shared/ui/loading-state/loading-state.component';
import { SkeletonComponent } from '@shared/ui/skeleton/skeleton.component';
import { ToastComponent } from '@shared/ui/toast/toast.component';

/**
 * UC-16 (transfer between wallets of the same currency), UC-17 (transfers
 * between different currencies are forbidden — enforced by the form's filter
 * and the server's validation). Editing a transfer is not part of the contract —
 * only create and delete (both operation halves are deleted atomically).
 */
@Component({
  selector: 'app-transfers',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    TransferFormComponent,
    ButtonComponent,
    CardComponent,
    EmptyStateComponent,
    ErrorStateComponent,
    FieldComponent,
    FieldControlDirective,
    LoadingStateComponent,
    SkeletonComponent,
    ToastComponent,
  ],
  templateUrl: './transfers-page.component.html',
  styleUrl: './transfers-page.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class TransfersComponent implements OnInit {
  private readonly transfersService = inject(TransfersApiService);
  private readonly walletsService = inject(WalletsApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly formatMoney = formatMoney;

  readonly wallets = signal<Wallet[]>([]);

  readonly transfers = signal<Transfer[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly isFormOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);

  readonly deleteError = signal<string | null>(null);

  readonly filterForm = this.formBuilder.nonNullable.group({
    walletId: [''],
  });

  /** Wallets for the form/filter dropdowns, with currency — used to filter the target wallet (UC-16). */
  readonly walletOptions = computed<TransferWalletOption[]>(() =>
    this.wallets().map((wallet) => ({ id: wallet.id, name: wallet.name, currencyId: wallet.currencyId })),
  );

  ngOnInit(): void {
    this.loadWallets();
    this.loadTransfers();
  }

  loadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
  }

  loadTransfers(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    const filters = this.filterForm.getRawValue();
    this.transfersService.list({ walletId: filters.walletId || undefined }).subscribe({
      next: (page) => {
        this.transfers.set(page.data);
        this.nextCursor.set(page.pagination.nextCursor);
        this.hasMore.set(page.pagination.hasMore);
        this.isLoading.set(false);
      },
      error: (error: unknown) => {
        this.loadError.set(extractErrorMessage(error, 'Не удалось загрузить список переводов.'));
        this.isLoading.set(false);
      },
    });
  }

  applyFilters(): void {
    this.loadTransfers();
  }

  resetFilters(): void {
    this.filterForm.reset({ walletId: '' });
    this.loadTransfers();
  }

  loadMore(): void {
    const cursor = this.nextCursor();
    if (!cursor || this.isLoading()) {
      return;
    }

    this.isLoading.set(true);
    const filters = this.filterForm.getRawValue();
    this.transfersService.list({ walletId: filters.walletId || undefined, cursor }).subscribe({
      next: (page) => {
        const result = appendPage(this.transfers(), page);
        this.transfers.set(result.items);
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

  openCreateForm(): void {
    this.submitError.set(null);
    this.isFormOpen.set(true);
  }

  cancelForm(): void {
    this.isFormOpen.set(false);
    this.submitError.set(null);
  }

  saveTransfer(request: CreateTransferRequest): void {
    if (this.isSubmitting()) {
      return;
    }
    this.isSubmitting.set(true);
    this.submitError.set(null);

    this.transfersService.create(request).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.cancelForm();
        this.loadTransfers();
        this.successMessage.set('Перевод создан');
      },
      error: (error: unknown) => {
        this.isSubmitting.set(false);
        this.submitError.set(
          extractErrorMessage(error, 'Не удалось создать перевод: возможно, кошельки имеют разные валюты (UC-17).'),
        );
      },
    });
  }

  deleteTransfer(transfer: Transfer): void {
    this.deleteError.set(null);

    this.transfersService.delete(transfer.id).subscribe({
      next: () => {
        this.loadTransfers();
        this.successMessage.set('Перевод удалён');
      },
      error: (error: unknown) => {
        this.deleteError.set(
          extractErrorMessage(
            error,
            'Не удалось удалить перевод: возможно, он уже учтен в истории баланса одного из кошельков.',
          ),
        );
      },
    });
  }

  walletName(walletId: string): string {
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? walletId;
  }
}
