import { Directive, inject } from '@angular/core';
import { FieldComponent } from './field.component';

/**
 * Wires a native input/textarea/select to the `app-field` wrapper: applies the id,
 * aria-describedby and aria-invalid computed in FieldComponent, and applies the
 * shared visual field style (`app-field__input` class, see field.component.scss).
 *
 * `FieldComponent` is injected as an ancestor in the DOM — Angular resolves DI
 * against the actual render hierarchy, which includes elements that arrived via
 * content projection, so a directive on `<input appFieldControl>` nested inside
 * `<app-field>` finds it with no extra wiring (the same trick `matInput`/
 * `mat-form-field` use).
 *
 * `disabled` is not synced automatically — when using ReactiveFormsModule, the
 * disabled state is managed by the form directly on this same element; duplicating
 * it here would create a race between two sources of truth.
 */
@Directive({
  selector: 'input[appFieldControl], textarea[appFieldControl], select[appFieldControl]',
  standalone: true,
  host: {
    class: 'app-field__input',
    '[attr.id]': 'field.controlId()',
    '[attr.aria-describedby]': 'field.describedBy()',
    '[attr.aria-invalid]': 'field.invalid() ? "true" : null',
  },
})
export class FieldControlDirective {
  protected readonly field = inject(FieldComponent);
}
