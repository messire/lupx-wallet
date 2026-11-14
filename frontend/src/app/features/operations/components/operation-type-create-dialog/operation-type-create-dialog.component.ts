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
  OperationBehaviorKind,
  OperationType,
} from '../../../../data-access/reference-data/reference-data-api.models';
import { ReferenceDataApiService } from '../../../../data-access/reference-data/reference-data-api.service';
import { fieldError } from '@shared/forms/field-error';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { DialogComponent } from '@shared/ui/dialog/dialog.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

/** A created operation type together with the code of its behavior kind (the form needs it to detect an adjustment). */
export interface CreatedOperationType {
  operationType: OperationType;
  behaviorKindCode: string;
}

/**
 * "+ добавить новый" for the operation-type dropdown (ADR-0009 §7): the required fields of an operation type — name
 * and behavior kind (POST /operation-types). Transfer is not offered: transfer operations are created by the
 * system (the same rule as the reference-data screen). On success emits the created type, which the caller adds
 * to its list and selects.
 */
@Component({
  selector: 'app-operation-type-create-dialog',
  standalone: true,
  imports: [
    ReactiveFormsModule,
    DialogComponent,
    FieldComponent,
    FieldControlDirective,
    SelectComponent,
    SelectOptionDirective,
    ButtonComponent,
  ],
  templateUrl: './operation-type-create-dialog.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OperationTypeCreateDialogComponent {
  private readonly referenceDataService = inject(ReferenceDataApiService);

  readonly open = input(false);
  readonly created = output<CreatedOperationType>();
  /** Closed without creating (Отмена, Escape, backdrop). */
  readonly dismissed = output<void>();

  readonly behaviorKinds = signal<OperationBehaviorKind[]>([]);
  readonly kindsLoadError = signal<string | null>(null);
  readonly isSubmitting = signal(false);
  readonly error = signal<string | null>(null);

  readonly creatableBehaviorKinds = computed(() =>
    this.behaviorKinds().filter((kind) => kind.code !== 'Transfer'),
  );

  readonly form = inject(FormBuilder).nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/)]],
    behaviorKindId: ['', Validators.required],
  });

  constructor() {
    effect(() => {
      if (this.open()) {
        untracked(() => {
          this.form.reset({ name: '', behaviorKindId: '' });
          this.error.set(null);
          if (this.behaviorKinds().length === 0) {
            this.loadBehaviorKinds();
          }
        });
      }
    });
  }

  loadBehaviorKinds(): void {
    this.kindsLoadError.set(null);
    this.referenceDataService.listOperationBehaviorKinds().subscribe({
      next: (kinds) => this.behaviorKinds.set(kinds),
      error: (error: unknown) =>
        this.kindsLoadError.set(extractErrorMessage(error, 'Не удалось загрузить виды поведения.')),
    });
  }

  nameError(): string | null {
    return fieldError(this.form.controls.name, 'Укажите название');
  }

  behaviorKindError(): string | null {
    return (
      this.kindsLoadError() ??
      fieldError(this.form.controls.behaviorKindId, 'Выберите вид поведения')
    );
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const { name, behaviorKindId } = this.form.getRawValue();
    this.isSubmitting.set(true);
    this.error.set(null);
    this.referenceDataService
      .createOperationType(name.trim(), behaviorKindId)
      .pipe(finalize(() => this.isSubmitting.set(false)))
      .subscribe({
        next: (operationType) =>
          this.created.emit({
            operationType,
            behaviorKindCode:
              this.behaviorKinds().find((kind) => kind.id === operationType.behaviorKindId)?.code ??
              '',
          }),
        error: (error: unknown) =>
          this.error.set(extractErrorMessage(error, 'Не удалось добавить тип операции.')),
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
