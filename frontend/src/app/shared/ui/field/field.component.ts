import { ChangeDetectionStrategy, Component, ViewEncapsulation, computed, input } from '@angular/core';

let nextFieldId = 0;

/**
 * Input field wrapper (docs/design/ui-kit.md §5 "Fields, select, checkbox, radio"):
 * a persistent label above the field, a helper/error below it; id/aria-describedby/
 * aria-invalid are computed here and applied to the nested control via
 * `FieldControlDirective` (see field-control.directive.ts). The component itself
 * does not render an input/textarea/select — the caller supplies it via
 * <ng-content>, so the component stays presentational and knows nothing about the
 * field's domain model.
 *
 * `ViewEncapsulation.None`: the field's visual style (`.app-field__input`) must
 * apply to the native control, which physically belongs to the caller component's
 * template (content projection does not change a DOM node's owner for emulated
 * encapsulation purposes) — so the styles are moved out from under the usual
 * isolation. Class names are specific enough (the `app-field__` prefix) to avoid
 * clashing with other components' styles.
 */
@Component({
  selector: 'app-field',
  standalone: true,
  templateUrl: './field.component.html',
  styleUrl: './field.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  encapsulation: ViewEncapsulation.None,
})
export class FieldComponent {
  readonly label = input.required<string>();
  readonly hint = input<string | null>(null);
  readonly error = input<string | null>(null);
  readonly required = input(false);
  readonly disabled = input(false);

  private readonly uid = `app-field-${nextFieldId++}`;

  readonly controlId = computed(() => `${this.uid}-control`);
  readonly hintId = computed(() => `${this.uid}-hint`);
  readonly errorId = computed(() => `${this.uid}-error`);
  readonly invalid = computed(() => !!this.error());

  /** The error takes priority over the helper text, but does not hide the entered value (it only affects the text below the field). */
  readonly describedBy = computed(() => (this.invalid() ? this.errorId() : this.hint() ? this.hintId() : null));
}
