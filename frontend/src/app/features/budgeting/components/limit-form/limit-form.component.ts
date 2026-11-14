import { ChangeDetectionStrategy, Component, OnInit, effect, inject, input, output } from '@angular/core';
import { FormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { amountValidator, nonNegativeAmountValidator } from '@shared/money/money';
import { fieldError } from '@shared/forms/field-error';
import { ExpenseCategory } from '../../../../data-access/budgeting/budgeting-api.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';
import { SelectComponent } from '@shared/ui/select/select.component';
import { SelectOptionDirective } from '@shared/ui/select/select-option.directive';

export interface LimitFormValue {
  categoryId: string;
  limit: string;
}

/**
 * Set or change the monthly limit of one expense category in a budget (PUT /budgets/{id}/limits/{categoryId},
 * ADR-0010 §5). The limit is in the budget currency and not less than zero; only an active category can get a new
 * limit. When editing, the category is fixed.
 */
@Component({
  selector: 'app-limit-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, FieldComponent, FieldControlDirective, SelectComponent, SelectOptionDirective],
  template: `
    <form class="entity-form" [formGroup]="form" (ngSubmit)="submit()" novalidate>
      <app-field
        label="Категория"
        [required]="true"
        [error]="categoryError()"
        [disabled]="fixedCategoryId() !== null"
        [hint]="categories().length === 0 ? 'Все активные категории уже имеют лимит — добавьте категорию на вкладке «Категории расходов»' : null"
      >
        <app-select formControlName="categoryId" placeholder="— выберите категорию —" emptyText="Нет категорий без лимита">
          @for (category of categories(); track category.id) {
            <app-select-option [value]="category.id" [label]="category.name" />
          }
        </app-select>
      </app-field>

      <app-field label="Лимит на месяц" [required]="true" [error]="limitError()">
        <div class="input-suffix">
          <input appFieldControl type="text" inputmode="decimal" placeholder="0.00" formControlName="limit" />
          @if (currencyCode()) {
            <span class="input-suffix__code">{{ currencyCode() }}</span>
          }
        </div>
      </app-field>

      @if (errorMessage()) {
        <p class="form-error" role="alert">{{ errorMessage() }}</p>
      }

      <div class="form-actions">
        <app-button type="submit" variant="primary" [loading]="isSubmitting()" loadingText="Сохранение…">Сохранить</app-button>
        <app-button type="button" variant="secondary" (click)="cancel.emit()">Отмена</app-button>
      </div>
    </form>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LimitFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);

  /** Categories offered in the select (for an edit — the one being edited). */
  readonly categories = input.required<ExpenseCategory[]>();
  readonly fixedCategoryId = input<string | null>(null);
  readonly initialLimit = input('');
  readonly currencyCode = input('');
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<LimitFormValue>();
  readonly cancel = output<void>();

  readonly form = this.formBuilder.nonNullable.group({
    categoryId: ['', Validators.required],
    limit: ['', [Validators.required, amountValidator, nonNegativeAmountValidator]],
  });

  constructor() {
    effect(() => {
      if (this.isSubmitting()) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
        if (this.fixedCategoryId()) {
          this.form.controls.categoryId.disable({ emitEvent: false });
        }
      }
    });
  }

  ngOnInit(): void {
    this.form.setValue({ categoryId: this.fixedCategoryId() ?? '', limit: this.initialLimit() });
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const value = this.form.getRawValue();
    this.save.emit({ categoryId: value.categoryId, limit: value.limit.trim() });
  }

  categoryError(): string | null {
    return fieldError(this.form.controls.categoryId, 'Выберите категорию');
  }

  limitError(): string | null {
    return fieldError(this.form.controls.limit, {
      required: 'Укажите лимит',
      amount: 'Сумма — число с точкой в качестве разделителя (например, 1234.56)',
      negativeAmount: 'Лимит не может быть отрицательным',
    });
  }
}
