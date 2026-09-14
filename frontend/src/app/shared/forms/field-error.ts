import { AbstractControl } from '@angular/forms';

/**
 * Field error text for `app-field` (ui-kit §5: an error is shown only after the
 * field has been interacted with — it stays hidden until `touched`, even if the
 * control is already invalid). A shared helper instead of repeating the same
 * `control.touched && control.invalid ? '…' : null` body in every form component.
 *
 * `messages` is either a single string (shown for any error on the control) or
 * an `{errorKey: text}` map, for fields with several validators that need
 * different messages (e.g. `required` and a custom `amount`).
 */
export function fieldError(control: AbstractControl | null | undefined, messages: string | Record<string, string>): string | null {
  if (!control || !control.touched || !control.invalid) {
    return null;
  }
  if (typeof messages === 'string') {
    return messages;
  }
  const errors = control.errors ?? {};
  for (const key of Object.keys(messages)) {
    if (key in errors) {
      return messages[key];
    }
  }
  return null;
}
