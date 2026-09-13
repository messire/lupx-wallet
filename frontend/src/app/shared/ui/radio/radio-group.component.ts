import { ChangeDetectionStrategy, Component, forwardRef, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';

let nextGroupId = 0;

/**
 * Radio group container (docs/design/ui-kit.md §5). Does not render the options
 * itself — they are passed via <ng-content> as `app-radio-option`s, which find
 * the group through DI (see radio-option.component.ts) and call `select()`.
 * ControlValueAccessor holds the group's currently selected value.
 *
 * `ariaLabelledBy`/`ariaLabel` — the group's accessible name (a role="radiogroup"
 * without a name is not announced by screen readers); they must sit on the same
 * element as the role itself, so this is a component input rather than an
 * arbitrary external `[attr.aria-labelledby]` (which would land on the
 * `<app-radio-group>` tag, not on the inner div carrying the role).
 */
@Component({
  selector: 'app-radio-group',
  standalone: true,
  template: `
    <div class="app-radio-group" role="radiogroup" [attr.aria-labelledby]="ariaLabelledBy()" [attr.aria-label]="ariaLabel()">
      <ng-content></ng-content>
    </div>
  `,
  styleUrl: './radio-group.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => RadioGroupComponent),
      multi: true,
    },
  ],
})
export class RadioGroupComponent implements ControlValueAccessor {
  readonly ariaLabelledBy = input<string | null>(null);
  readonly ariaLabel = input<string | null>(null);

  readonly name = `app-radio-group-${nextGroupId++}`;
  readonly value = signal<string | null>(null);
  readonly disabled = signal(false);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};

  writeValue(value: string | null): void {
    this.value.set(value);
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  select(value: string): void {
    this.value.set(value);
    this.onChange(value);
    this.onTouched();
  }
}
