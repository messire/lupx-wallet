import {
  ChangeDetectionStrategy,
  Component,
  untracked,
  effect,
  inject,
  input,
  output,
  signal,
} from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { ReferenceItem } from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { fieldError } from '@shared/forms/field-error';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

/**
 * "+ добавить новый" for the wallet-type dropdown (ADR-0009 §7): a small dialog with the one required field of a
 * wallet type — its name (POST /wallet-types). On success emits the created entry, which the caller adds to its
 * list and selects in the field that opened the dialog. A server error (e.g. 409, a duplicate name) stays in the
 * dialog with the typed name kept, so it can be fixed and resubmitted.
 */
@Component({
  selector: 'app-wallet-type-create-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DialogComponent,
    FieldComponent,
    FieldControlDirective,
    ButtonComponent,
  ],
  templateUrl: './wallet-type-create-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class WalletTypeCreateDialogComponent {
  private readonly referenceDataService = inject(ReferenceDataApiService);

  readonly open = input(false);
  readonly created = output<ReferenceItem>();
  /** Closed without creating (Отмена, Escape, backdrop). */
  readonly dismissed = output<void>();

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/)]],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.form.reset({ name: '' });
          this.error.set(null);
        });
      }
    });
  }

  nameError(): string | null {
    return fieldError(this.form.controls.name, 'Укажите название');
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    this.isSubmitting.set(true);
    this.error.set(null);
    this.referenceDataService
      .createWalletType(this.form.controls.name.value.trim())
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (item) => this.created.emit(item),
        error: (error: unknown) =>
          this.error.set(extractErrorMessage(error, 'Не удалось добавить тип кошелька.')),
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
