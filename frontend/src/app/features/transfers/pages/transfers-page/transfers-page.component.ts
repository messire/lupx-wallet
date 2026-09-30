import { ChangeDetectionStrategy, Component, OnInit, computed, inject, signal, viewChild } from '@angular/core';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { appendPage } from '@shared/pagination/cursor-page';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { formatMoney } from '@shared/money/money';
import { Currency } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { Wallet } from '../../../../data-access/wallets/wallets-api.models';
import { WalletsApiService } from '../../../../data-access/wallets/wallets-api.service';
import { CreateTransferRequest, Transfer } from '../../../../data-access/transfers/transfers-api.models';
import { TransfersApiService } from '../../../../data-access/transfers/transfers-api.service';
import { TransferFormComponent, TransferWalletOption } from '../../components/transfer-form/transfer-form.component';
import { BadgeComponent } from '@shared/ui/badge/badge.component';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CardComponent } from '@shared/ui/card/card.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
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
 * only create, soft delete and restore (ADR-0002: both operation halves are marked
 * deleted/restored atomically, any transfer date; deleted transfers stay in the list).
 */
@Component({
  selector: 'app-transfers',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    RouterLink,
    TransferFormComponent,
    BadgeComponent,
    ButtonComponent,
    CardComponent,
    DialogComponent,
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
  private readonly referenceDataService = inject(ReferenceDataApiService);
  private readonly formBuilder = inject(FormBuilder);

  protected readonly formatMoney = formatMoney;

  private readonly transferFormRef = viewChild(TransferFormComponent);

  readonly wallets = signal<Wallet[]>([]);
  readonly currencies = signal<Currency[]>([]);

  readonly transfers = signal<Transfer[]>([]);
  readonly nextCursor = signal<string | null>(null);
  readonly hasMore = signal(false);

  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly isFormOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);
  readonly successMessage = signal<string | null>(null);
  /** screen-spec.md "Переводы": Escape/«Отмена» на изменённой форме требует подтверждения. */
  readonly discardConfirmOpen = signal(false);

  readonly deleteError = signal<string | null>(null);
  readonly isDeleting = signal(false);
  readonly pendingDeleteTransfer = signal<Transfer | null>(null);
  /** screen-spec.md "Переводы": действия строки находятся под «···» (тот же паттерн, что в Operations). */
  readonly pendingRowActionsTransfer = signal<Transfer | null>(null);
  private nextRowAction: { type: 'delete'; transfer: Transfer } | null = null;
  /** Error of the last restore attempt (shown above the list; restore has no confirm dialog). */
  readonly restoreError = signal<string | null>(null);
  readonly isRestoring = signal(false);

  readonly filterForm = this.formBuilder.nonNullable.group({
    walletId: [''],
  });

  /** screen-spec.md "Переводы": пустой результат фильтра предлагает сброс, снимок на
   *  момент последней успешно применённой загрузки (по образцу Operations). */
  readonly hasActiveFilters = signal(false);

  /** Wallets for the form/filter dropdowns, with currency code and accounting start date. */
  readonly walletOptions = computed<TransferWalletOption[]>(() => {
    const currencies = new Map(this.currencies().map((currency) => [currency.id, currency.code]));
    return this.wallets().map((wallet) => ({
      id: wallet.id,
      name: wallet.name,
      currencyId: wallet.currencyId,
      currencyCode: currencies.get(wallet.currencyId) ?? '',
      accountingStartDate: wallet.accountingStartDate,
    }));
  });

  ngOnInit(): void {
    this.loadWallets();
    this.loadCurrencies();
    this.loadTransfers();
  }

  loadWallets(): void {
    this.walletsService.list({ includeArchived: true, limit: 100 }).subscribe((page) => this.wallets.set(page.data));
  }

  loadCurrencies(): void {
    this.referenceDataService.listCurrencies({ limit: 100 }).subscribe((page) => this.currencies.set(page.data));
  }

  loadTransfers(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    const filters = this.filterForm.getRawValue();
    this.hasActiveFilters.set(!!filters.walletId);
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
    this.discardConfirmOpen.set(false);
  }

  /**
   * The form's own "Отмена" button — unlike Escape/backdrop (native `cancel`
   * event, handled by `onFormDismissRequested`), this is a direct call, so the
   * dirty check happens here rather than via `event.preventDefault()`.
   */
  requestCloseForm(): void {
    if (this.transferFormRef()?.form.dirty) {
      this.discardConfirmOpen.set(true);
    } else {
      this.cancelForm();
    }
  }

  /** Escape/backdrop on the dialog (native `cancel` event) — same dirty check, but must
   *  call `preventDefault()` to stop the native close before the confirm sub-dialog opens. */
  onFormDismissRequested(event: Event): void {
    if (this.transferFormRef()?.form.dirty) {
      event.preventDefault();
      this.discardConfirmOpen.set(true);
    } else {
      this.cancelForm();
    }
  }

  continueEditing(): void {
    this.discardConfirmOpen.set(false);
  }

  confirmDiscard(): void {
    this.discardConfirmOpen.set(false);
    this.cancelForm();
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
          extractErrorMessage(error, 'Не удалось создать перевод: возможно, кошельки имеют разные валюты.'),
        );
      },
    });
  }

  openRowActions(transfer: Transfer): void {
    this.pendingRowActionsTransfer.set(transfer);
  }

  closeRowActions(): void {
    this.nextRowAction = null;
    this.pendingRowActionsTransfer.set(null);
  }

  deleteFromRowActions(transfer: Transfer): void {
    this.nextRowAction = { type: 'delete', transfer };
    this.pendingRowActionsTransfer.set(null);
  }

  /** ADR-0002 §3: restoring is reversible, so it runs immediately — no confirmation. */
  restoreFromRowActions(transfer: Transfer): void {
    this.closeRowActions();
    if (this.isRestoring()) {
      return;
    }
    this.isRestoring.set(true);
    this.restoreError.set(null);

    this.transfersService.restore(transfer.id).subscribe({
      next: () => {
        this.isRestoring.set(false);
        this.loadTransfers();
        this.successMessage.set('Перевод восстановлен');
      },
      error: (error: unknown) => {
        this.isRestoring.set(false);
        // 404 (not found) / 409 (the transfer is not deleted) — contract openapi.yaml.
        this.restoreError.set(extractErrorMessage(error, 'Не удалось восстановить перевод.'));
      },
    });
  }

  /**
   * Wired to the row-actions dialog's `(closed)` — opens the delete-confirm dialog
   * only after the row-actions dialog has actually finished closing (mirrors
   * Operations' `onRowActionsDialogClosed`, see that file for why the synchronous
   * alternative races two independent `DialogComponent`/`<dialog>` effects).
   */
  onRowActionsDialogClosed(): void {
    const next = this.nextRowAction;
    this.nextRowAction = null;
    if (next?.type === 'delete') {
      this.requestDeleteConfirm(next.transfer);
    }
  }

  requestDeleteConfirm(transfer: Transfer): void {
    this.deleteError.set(null);
    this.pendingDeleteTransfer.set(transfer);
  }

  cancelDeleteConfirm(): void {
    if (this.isDeleting()) {
      return;
    }
    this.pendingDeleteTransfer.set(null);
    this.deleteError.set(null);
  }

  /** Escape/backdrop on the delete-confirm dialog (native `cancel` event) — mirrors
   *  `onFormDismissRequested`: must call `preventDefault()` to stop the native
   *  `<dialog>` from closing while a DELETE request is in flight (MAJOR-2). Without
   *  it, the dialog closes natively on Escape but `pendingDeleteTransfer`/`open`
   *  stay unchanged, so a subsequent 409 error has nowhere visible to render, and
   *  re-opening the confirm dialog for the same transfer (same signal value, no
   *  change) fails to re-trigger `DialogComponent`'s `showModal()`. */
  onDeleteDismissRequested(event: Event): void {
    if (this.isDeleting()) {
      event.preventDefault();
      return;
    }
    this.pendingDeleteTransfer.set(null);
    this.deleteError.set(null);
  }

  confirmDeleteTransfer(): void {
    const transfer = this.pendingDeleteTransfer();
    if (!transfer || this.isDeleting()) {
      return;
    }
    this.isDeleting.set(true);
    this.deleteError.set(null);

    this.transfersService.delete(transfer.id).subscribe({
      next: () => {
        this.isDeleting.set(false);
        this.pendingDeleteTransfer.set(null);
        this.loadTransfers();
        this.successMessage.set('Перевод удалён');
      },
      error: (error: unknown) => {
        this.isDeleting.set(false);
        // 404 / 409 (the transfer is already deleted) — contract openapi.yaml; the date does not matter (ADR-0002 §2).
        this.deleteError.set(extractErrorMessage(error, 'Не удалось удалить перевод.'));
      },
    });
  }

  walletName(walletId: string): string {
    return this.wallets().find((wallet) => wallet.id === walletId)?.name ?? walletId;
  }

  /** Currency code by wallet id — for showing an amount with its currency explicitly (ui-kit §5). */
  walletCurrencyCode(walletId: string): string | undefined {
    const currencyId = this.wallets().find((wallet) => wallet.id === walletId)?.currencyId;
    return this.currencies().find((currency) => currency.id === currencyId)?.code;
  }
}
