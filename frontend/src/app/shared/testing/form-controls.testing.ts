import { DebugElement } from '@angular/core';
import { By } from '@angular/platform-browser';
import { DateInputComponent } from '../ui/date-input/date-input.component';

/**
 * Unit-test helpers for the themed form controls (`app-select`, `app-date-input`, ADR-0009 §6) — the DOM steps a
 * user takes, in one place instead of each spec poking at their internals. Used only by `*.spec.ts`.
 */

function selectHost(scope: ParentNode, controlName?: string): HTMLElement {
  const selector = controlName ? `app-select[formcontrolname="${controlName}"]` : 'app-select';
  const host = scope.querySelector(selector) as HTMLElement | null;
  if (!host) {
    throw new Error(`No ${selector} found`);
  }
  return host;
}

/** The `<button role="combobox">` of an `app-select` (the first one in `scope`, or the one bound to `controlName`). */
export function selectTrigger(scope: ParentNode, controlName?: string): HTMLButtonElement {
  return selectHost(scope, controlName).querySelector('button[role="combobox"]') as HTMLButtonElement;
}

/** Opens the dropdown and returns the texts of its rows (including a trailing "+ добавить новый"). */
export function selectOptionTexts(scope: ParentNode, controlName: string | undefined, detectChanges: () => void): string[] {
  const host = selectHost(scope, controlName);
  const trigger = host.querySelector('button[role="combobox"]') as HTMLButtonElement;
  if (trigger.getAttribute('aria-expanded') !== 'true') {
    trigger.click();
    detectChanges();
  }
  const texts = Array.from(host.querySelectorAll('[role="option"]')).map((row) => row.textContent?.trim() ?? '');
  trigger.click(); // close again
  detectChanges();
  return texts;
}

/** Opens the dropdown and clicks the row with the given value, as a user would. */
export function chooseSelectValue(scope: ParentNode, controlName: string | undefined, value: string, detectChanges: () => void): void {
  const host = selectHost(scope, controlName);
  (host.querySelector('button[role="combobox"]') as HTMLButtonElement).click();
  detectChanges();
  const row = (Array.from(host.querySelectorAll('[role="option"]')) as HTMLElement[]).find((option) => option.dataset['value'] === value);
  if (!row) {
    throw new Error(`No option with value "${value}"`);
  }
  row.click();
  detectChanges();
}

/** The text box of an `app-date-input` (the first one in `scope`, or the one bound to `controlName`). */
export function dateTextInput(scope: ParentNode, controlName?: string): HTMLInputElement {
  const selector = controlName ? `app-date-input[formcontrolname="${controlName}"] input` : 'app-date-input input';
  const input = scope.querySelector(selector) as HTMLInputElement | null;
  if (!input) {
    throw new Error(`No ${selector} found`);
  }
  return input;
}

/** Types a date (`yyyy-MM-dd` or `дд.мм.гггг`) into an `app-date-input` text box. */
export function typeDate(input: HTMLInputElement, text: string): void {
  input.value = text;
  input.dispatchEvent(new Event('input'));
}

/** The `min`/`max` an `app-date-input` currently limits its calendar to. */
export function dateInputBounds(root: DebugElement, controlName?: string): { min: string | null; max: string | null } {
  const debug = root
    .queryAll(By.directive(DateInputComponent))
    .find((element) => !controlName || element.attributes['formControlName'] === controlName || element.attributes['formcontrolname'] === controlName);
  if (!debug) {
    throw new Error('No app-date-input found');
  }
  const component = debug.componentInstance as DateInputComponent;
  return { min: component.min() ?? null, max: component.max() ?? null };
}
