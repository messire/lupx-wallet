import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal, untracked } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { finalize } from 'rxjs';
import { extractErrorMessage } from '../../../../core/http/problem-details';
import { Creditor } from '../../../../data-access/debts/debts-api.models';
import { DebtsApiService } from '../../../../data-access/debts/debts-api.service';
import { fieldError } from '@shared/forms/field-error';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

/**
 * "+ добавить кредитора" for the creditor dropdown of the debt/loan form (ADR-0009 §7): a small dialog with the
 * creditor's name (POST /creditors). On success emits the created creditor, which the caller adds to its list and
 * selects in the form. A server error stays in the dialog with the typed name kept.
 */
@Component({
  selector: 'app-creditor-create-dialog',
  standalone: true,
  imports: [ReactiveFormsModule, DialogComponent, FieldComponent, FieldControlDirective, ButtonComponent],
  template: `
    <app-dialog [open]="open()" titleText="Новый кредитор" size="sm" (dismissRequested)="onDismissRequested($event)" (closed)="cancel()">
      @if (open()) {
        <form [formGroup]="form" (ngSubmit)="submit()" novalidate>
          <app-field label="Название" [required]="true" [error]="nameError()">
            <input appFieldControl type="text" formControlName="name" maxlength="200" placeholder="Например, «Банк» или имя человека" />
          </app-field>

          @if (error(); as message) {
            <p class="form-error" role="alert">{{ message }}</p>
          }

          <div class="form-actions">
            <app-button type="submit" variant="primary" [loading]="isSubmitting()" loadingText="Сохранение…">Добавить</app-button>
            <app-button type="button" variant="secondary" [disabled]="isSubmitting()" (click)="cancel()">Отмена</app-button>
          </div>
        </form>
      }
    </app-dialog>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreditorCreateDialogComponent {
  private readonly debtsService = inject(DebtsApiService);

  readonly open = input(false);
  readonly created = output<Creditor>();
  /** Closed without creating (Отмена, Escape, backdrop). */
  readonly dismissed = output<void>();

  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
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
    this.debtsService
      .createCreditor(this.form.controls.name.value.trim())
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (creditor) => this.created.emit(creditor),
        error: (error: unknown) => this.error.set(extractErrorMessage(error, 'Не удалось добавить кредитора.')),
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
