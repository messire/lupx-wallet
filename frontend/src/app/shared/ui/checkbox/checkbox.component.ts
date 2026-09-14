import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextCheckboxId = 0;

/**
 * Checkbox with a visible clickable label (docs/design/ui-kit.md §5): the click
 * area is at least 44 px, and the checked state is shown both by a changing
 * background/outline and by its own checkmark (the state is not conveyed by
 * color alone). ControlValueAccessor — like `app-currency-select`, works with
 * formControlName/ngModel.
 */
@Component({
  selector: 'app-checkbox',
  standalone: true,
  templateUrl: './checkbox.component.html',
  styleUrl: './checkbox.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => CheckboxComponent),
      multi: true,
    },
  ],
})
export class CheckboxComponent implements ControlValueAccessor {
  readonly label = input.required<string>();

  readonly inputId = `app-checkbox-${nextCheckboxId++}`;
  readonly checked = signal(false);
  readonly disabled = signal(false);

  private onChange: (value: boolean) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: boolean | null): void {
    this.checked.set(!!value);
  }

  registerOnChange(fn: (value: boolean) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  onInputChange(value: boolean): void {
    this.checked.set(value);
    this.onChange(value);
  }

  onBlur(): void {
    this.onTouched();
  }
}
