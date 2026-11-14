import { ChangeDetectionStrategy, Component, OnInit, computed, effect, inject, input, output } from '@angular/core';
import { FormBuilder, FormControl, FormRecord, ReactiveFormsModule, Validators } from '@angular/forms';
import { fieldError } from '@shared/forms/field-error';
import { ExpenseCategory } from '../../../../data-access/budgeting/budgeting-api.models';
import { OperationType } from '../../../../data-access/reference-data/reference-data-api.models';
import { ButtonComponent } from '@shared/ui/button/button.component';
import { CheckboxComponent } from '@shared/ui/checkbox/checkbox.component';
import { FieldComponent } from '@shared/ui/field/field.component';
import { FieldControlDirective } from '@shared/ui/field/field-control.directive';

export interface CategoryFormValue {
  name: string;
  operationTypeIds: string[];
}

/** An expense operation type offered in the form; `ownerName` — the other category it already belongs to. */
interface TypeChoice {
  type: OperationType;
  ownerName: string | null;
}

/**
 * Expense category editor (#11, ADR-0010 §5, В1): a name and the set of expense operation types grouped into it.
 * A type belongs to at most one category — types of other categories are shown disabled with the owner's name
 * (the server answers 409 otherwise). Inactive types stay selectable only if they already belong to this category.
 */
@Component({
  selector: 'app-category-form',
  standalone: true,
  imports: [ReactiveFormsModule, ButtonComponent, CheckboxComponent, FieldComponent, FieldControlDirective],
  templateUrl: './category-form.component.html',
  styleUrl: './category-form.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CategoryFormComponent implements OnInit {
  private readonly formBuilder = inject(FormBuilder);

  /** Expense operation types, inactive ones included. */
  readonly expenseTypes = input.required<OperationType[]>();
  readonly categories = input.required<ExpenseCategory[]>();
  readonly category = input<ExpenseCategory | null>(null);
  readonly isSubmitting = input(false);
  readonly errorMessage = input<string | null>(null);

  readonly save = output<CategoryFormValue>();
  readonly cancel = output<void>();

  readonly types = new FormRecord<FormControl<boolean>>({});
  readonly form = this.formBuilder.nonNullable.group({
    name: ['', [Validators.required, Validators.pattern(/\S/), Validators.maxLength(200)]],
    types: this.types,
  });

  readonly choices = computed<TypeChoice[]>(() => {
    const own = new Set(this.category()?.operationTypeIds ?? []);
    const owners = new Map<string, string>();
    for (const category of this.categories()) {
      if (category.id !== this.category()?.id) {
        category.operationTypeIds.forEach((id) => owners.set(id, category.name));
      }
    }
    return this.expenseTypes()
      .filter((type) => type.isActive || own.has(type.id))
      .map((type) => ({ type, ownerName: owners.get(type.id) ?? null }));
  });

  constructor() {
    effect(() => {
      const disabled = this.isSubmitting();
      if (disabled) {
        this.form.disable({ emitEvent: false });
      } else {
        this.form.enable({ emitEvent: false });
      }
      if (!disabled) {
        for (const choice of this.choices()) {
          if (choice.ownerName) {
            this.types.controls[choice.type.id]?.disable({ emitEvent: false });
          }
        }
      }
    });
  }

  ngOnInit(): void {
    const category = this.category();
    const own = new Set(category?.operationTypeIds ?? []);
    for (const choice of this.choices()) {
      this.types.addControl(choice.type.id, new FormControl(own.has(choice.type.id), { nonNullable: true }));
    }
    if (category) {
      this.form.controls.name.setValue(category.name);
    }
  }

  submit(): void {
    if (this.isSubmitting()) {
      return;
    }
    if (this.form.invalid) {
      this.form.markAllAsTouched();
      return;
    }
    const selected = Object.entries(this.types.getRawValue())
      .filter(([, checked]) => checked)
      .map(([id]) => id);
    this.save.emit({ name: this.form.controls.name.value.trim(), operationTypeIds: selected });
  }

  choiceLabel(choice: TypeChoice): string {
    if (choice.ownerName) {
      return `${choice.type.name} — уже в «${choice.ownerName}»`;
    }
    return choice.type.isActive ? choice.type.name : `${choice.type.name} (деактивирован)`;
  }

  nameError(): string | null {
    return fieldError(this.form.controls.name, 'Укажите название');
  }
}
