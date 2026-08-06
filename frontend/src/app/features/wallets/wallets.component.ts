import { HttpErrorResponse } from '@angular/common/http';
import { Component, inject, OnInit, signal } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { Router } from '@angular/router';
import { AuthService } from '../../core/auth/auth.service';
import { Wallet } from './wallets.models';
import { WalletsService } from './wallets.service';

/**
 * UC-01 (создание) и UC-07 (список) — docs/requirements/user-scenarios.md.
 * walletTypeId/currencyId вводятся как обычный текст (UUID), так как модуль
 * ReferenceData со справочниками типов кошельков и валют — предмет следующего
 * среза; выпадающих списков для их выбора пока нет.
 */
@Component({
  selector: 'app-wallets',
  standalone: true,
  imports: [ReactiveFormsModule],
  templateUrl: './wallets.component.html',
  styleUrl: './wallets.component.scss',
})
export class WalletsComponent implements OnInit {
  private readonly walletsService = inject(WalletsService);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly formBuilder = inject(FormBuilder);

  readonly wallets = signal<Wallet[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);

  readonly isFormOpen = signal(false);
  readonly isSubmitting = signal(false);
  readonly submitError = signal<string | null>(null);

  readonly form = this.formBuilder.nonNullable.group({
    name: ['', Validators.required],
    walletTypeId: ['', Validators.required],
    currencyId: ['', Validators.required],
    initialBalanceAmount: [0, Validators.required],
    accountingStartDate: [this.today(), Validators.required],
  });

  ngOnInit(): void {
    this.loadWallets();
  }

  loadWallets(): void {
    this.isLoading.set(true);
    this.loadError.set(null);

    this.walletsService.list().subscribe({
      next: (page) => {
        this.wallets.set(page.data);
        this.isLoading.set(false);
      },
      error: () => {
        this.loadError.set('Не удалось загрузить список кошельков.');
        this.isLoading.set(false);
      },
    });
  }

  openForm(): void {
    this.submitError.set(null);
    this.isFormOpen.set(true);
  }

  cancelForm(): void {
    this.isFormOpen.set(false);
    this.form.reset({ name: '', walletTypeId: '', currencyId: '', initialBalanceAmount: 0, accountingStartDate: this.today() });
  }

  submit(): void {
    if (this.form.invalid || this.isSubmitting()) {
      return;
    }

    this.submitError.set(null);
    this.isSubmitting.set(true);

    this.walletsService.create(this.form.getRawValue()).subscribe({
      next: () => {
        this.isSubmitting.set(false);
        this.cancelForm();
        this.loadWallets();
      },
      error: (error: HttpErrorResponse) => {
        this.isSubmitting.set(false);
        this.submitError.set(error.error?.detail ?? 'Не удалось создать кошелек.');
      },
    });
  }

  logout(): void {
    this.authService.logout();
    this.router.navigateByUrl('/login');
  }

  private today(): string {
    return new Date().toISOString().slice(0, 10);
  }
}
