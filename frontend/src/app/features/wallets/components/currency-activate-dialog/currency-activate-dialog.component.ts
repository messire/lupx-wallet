import {
  ChangeDetectionStrategy,
  Component,
  untracked,
  computed,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import {
  AvailableCurrency,
  Currency,
} from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { CurrencyOption } from '@shared/currencies/currency-option';
import { CurrencySelectComponent } from '@shared/currencies/currency-select/currency-select.component';
import { fieldError } from '@shared/forms/field-error';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { FieldComponent } from '@shared/ui/field/field.component';

/**
 * "+ добавить новый" for the active-currency dropdown of "Сменить валюту" (ADR-0009 §7). An active currency has
 * one required field — its code, picked from the list of currencies available from the exchange-rate service
 * that are not active yet (POST /currencies, ADR-0004 §7; the name comes from that list on the server). On
 * success emits the created active currency, which the caller selects.
 */
@Component({
  selector: 'app-currency-activate-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DialogComponent,
    FieldComponent,
    CurrencySelectComponent,
    ButtonComponent,
  ],
  templateUrl: './currency-activate-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CurrencyActivateDialogComponent {
  private readonly referenceDataService = inject(ReferenceDataApiService);

  readonly open = input(false);
  /** Already active currencies — not offered again. */
  readonly activeCurrencies = input<readonly Currency[]>([]);
  readonly created = output<Currency>();
  /** Closed without adding (Отмена, Escape, backdrop). */
  readonly dismissed = output<void>();

  readonly available = signal<AvailableCurrency[]>([]);
  readonly isLoading = signal(false);
  readonly loadError = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly options = computed<CurrencyOption[]>(() => {
    const active = new Set(this.activeCurrencies().map((currency) => currency.code));
    return this.available()
      .filter((currency) => !currency.isActive && !active.has(currency.code))
      .map(({ code, name }) => ({ code, name }));
  });

  readonly emptyHint = computed(() =>
    !this.isLoading() && !this.loadError() && this.options().length === 0
      ? this.available().length === 0
        ? 'Список валют ещё не загружен из сервиса курсов. Обновите курсы на экране «Курсы валют».'
        : 'Все доступные валюты уже активны.'
      : null,
  );

  readonly form = inject(FormBuilder).group({
    code: [null as string | null, Validators.required],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.form.reset({ code: null });
          this.error.set(null);
          this.load();
        });
      }
    });
  }

  load(): void {
    this.isLoading.set(true);
    this.loadError.set(null);
    this.referenceDataService
      .listAvailableCurrencies()
      .pipe(finalize(() => this.isLoading.set(false)))
      .subscribe({
        next: (currencies) => this.available.set(currencies),
        error: (error: unknown) =>
          this.loadError.set(
            extractErrorMessage(error, 'Не удалось загрузить список доступных валют.'),
          ),
      });
  }

  codeError(): string | null {
    return this.loadError() ?? fieldError(this.form.controls.code, 'Выберите валюту');
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }
    const code = this.form.controls.code.value;
    if (!code) {
      this.form.markAllAsTouched();
      return;
    }
    this.isSubmitting.set(true);
    this.error.set(null);
    this.referenceDataService
      .createCurrency(code)
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (currency) => this.created.emit(currency),
        error: (error: unknown) =>
          this.error.set(extractErrorMessage(error, 'Не удалось добавить валюту.')),
      });
  }

  onDismissRequested(event: Event): void {
    if (this.isSubmitting()) {
      event.preventDefault();
    }
  }

  cancel(): void {
    if (!this.isSubmitting()) {
      this.dismissed.emit();
    }
  }
}
