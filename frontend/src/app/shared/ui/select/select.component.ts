import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  computed,
  contentChildren,
  forwardRef,
  inject,
  input,
  output,
  signal,
  viewChild,
} from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { AnchoredPanelDirective } from '../popover/anchored-panel.directive';
import { FieldComponent } from '../field/field.component';
import { SelectOptionDirective } from './select-option.directive';

let nextId = 0;

/** One row of the open list: a value, or the trailing "+ добавить новый" action. */
type SelectItem =
  | { kind: 'option'; value: string; label: string; disabled: boolean }
  | { kind: 'create'; label: string };

/** Typeahead buffer is reset after this pause between key presses, ms (the native select behaves the same way). */
const TYPEAHEAD_RESET_MS = 700;

/**
 * Single-value dropdown in the application theme (ui-kit §5 "Поля, select", ADR-0009 §6) — replaces the native
 * `<select>`, whose open list is drawn by the operating system (a grey Windows popup on the dark indigo UI).
 *
 * ARIA "select-only combobox" pattern: the trigger is a `<button role="combobox">` (a button is labelable, so the
 * surrounding `app-field`'s `<label for>` names it), focus stays on it while the list is open and the active row
 * is announced through `aria-activedescendant`. Keyboard: Down/Up/Enter/Space/Alt+Down open the list; with the
 * list open — Up/Down, Home/End, Enter/Space to choose, Escape/Tab to close; typing letters jumps to the first
 * matching option (also when closed, like the native control).
 *
 * Options are declared with `<app-select-option [value] [label]>` children. A value that matches no option
 * (e.g. the list is still loading) shows the placeholder.
 *
 * `createLabel` adds a last row "+ добавить новый" for a dropdown backed by a reference list: choosing it does
 * not change the value, closes the list and emits `createRequested` — the caller opens a dialog with the new
 * entry's fields and, after creating it, sets the control to the new id.
 *
 * Inside an `app-field` the trigger takes its id, `aria-describedby`, `aria-invalid` and `aria-required` from the
 * field, exactly like `FieldControlDirective` does for native controls. Outside a field, use `ariaLabel`.
 */
@Component({
  selector: 'app-select',
  standalone: true,
  imports: [AnchoredPanelDirective],
  templateUrl: './select.component.html',
  styleUrl: './select.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => SelectComponent),
      multi: true,
    },
  ],
})
export class SelectComponent implements ControlValueAccessor {
  protected readonly field = inject(FieldComponent, { optional: true });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly triggerRef = viewChild.required<ElementRef<HTMLButtonElement>>('trigger');
  private readonly optionDirectives = contentChildren(SelectOptionDirective, { descendants: true });

  readonly placeholder = input('— выберите —');
  /** Text of the trailing "add a new reference entry" row; no row when `null`. */
  readonly createLabel = input<string | null>(null);
  /** Shown in the open list when there are no options (the create row, if any, is still offered). */
  readonly emptyText = input('Список пуст');
  /** Accessible name when no `app-field` label points at the trigger. */
  readonly ariaLabel = input<string | null>(null);

  /** The "+ добавить новый" row was chosen. */
  readonly createRequested = output<void>();

  readonly listboxId = `app-select-listbox-${nextId++}`;
  readonly isOpen = signal(false);
  readonly highlightedIndex = signal(-1);
  readonly disabled = signal(false);
  readonly value = signal<string | null>(null);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};
  private typeahead = '';
  private typeaheadTimer: ReturnType<typeof setTimeout> | null = null;

  readonly options = computed(() =>
    this.optionDirectives().map((option) => ({
      kind: 'option' as const,
      value: option.value(),
      label: option.label(),
      disabled: option.disabled(),
    })),
  );

  readonly items = computed<SelectItem[]>(() => {
    const createLabel = this.createLabel();
    return createLabel
      ? [...this.options(), { kind: 'create', label: createLabel }]
      : this.options();
  });

  readonly selectedOption = computed(
    () => this.options().find((option) => option.value === this.value()) ?? null,
  );

  readonly activeOptionId = computed(() => {
    const index = this.highlightedIndex();
    return this.isOpen() && index >= 0 && index < this.items().length ? this.optionId(index) : null;
  });

  readonly triggerElement = computed(() => this.triggerRef().nativeElement);

  optionId(index: number): string {
    return `${this.listboxId}-option-${index}`;
  }

  // ---- ControlValueAccessor ----

  writeValue(value: string | null): void {
    this.value.set(value ?? null);
  }

  registerOnChange(fn: (value: string) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
    if (isDisabled) {
      this.isOpen.set(false);
    }
  }

  /** Moves keyboard focus to the trigger without opening the list. */
  focus(): void {
    this.triggerRef().nativeElement.focus();
  }

  // ---- Interaction ----

  onTriggerClick(): void {
    if (this.disabled()) {
      return;
    }
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  onBlur(event: FocusEvent): void {
    const next = event.relatedTarget as Node | null;
    if (next && this.host.nativeElement.contains(next)) {
      return;
    }
    if (this.isOpen()) {
      this.isOpen.set(false);
    }
    this.onTouched();
  }

  onKeydown(event: KeyboardEvent): void {
    if (this.disabled()) {
      return;
    }
    const key = event.key;

    if (!this.isOpen()) {
      if (key === 'ArrowDown' || key === 'ArrowUp' || key === 'Enter' || key === ' ') {
        event.preventDefault();
        this.open();
      } else if (this.isTypeaheadKey(event)) {
        event.preventDefault();
        const index = this.findByTypeahead(key);
        if (index >= 0) {
          this.choose(index);
        }
      }
      return;
    }

    switch (key) {
      case 'ArrowDown':
        event.preventDefault();
        this.moveHighlight(1);
        break;
      case 'ArrowUp':
        event.preventDefault();
        if (event.altKey) {
          this.choose(this.highlightedIndex());
        } else {
          this.moveHighlight(-1);
        }
        break;
      case 'Home':
        event.preventDefault();
        this.highlightedIndex.set(this.firstEnabled(0, 1));
        break;
      case 'End':
        event.preventDefault();
        this.highlightedIndex.set(this.firstEnabled(this.items().length - 1, -1));
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.choose(this.highlightedIndex());
        break;
      case 'Escape':
        event.preventDefault();
        event.stopPropagation(); // an open list inside a dialog closes first; the dialog stays open
        this.close();
        break;
      case 'Tab':
        this.close();
        break;
      default:
        if (this.isTypeaheadKey(event)) {
          event.preventDefault();
          const index = this.findByTypeahead(key);
          if (index >= 0) {
            this.highlightedIndex.set(index);
          }
        }
    }
  }

  onOptionClick(index: number): void {
    this.choose(index);
  }

  onOptionHover(index: number): void {
    if (!this.isItemDisabled(index)) {
      this.highlightedIndex.set(index);
    }
  }

  isItemDisabled(index: number): boolean {
    const item = this.items()[index];
    return !item || (item.kind === 'option' && item.disabled);
  }

  private open(): void {
    const selectedIndex = this.items().findIndex(
      (item) => item.kind === 'option' && item.value === this.value(),
    );
    this.highlightedIndex.set(selectedIndex >= 0 ? selectedIndex : this.firstEnabled(0, 1));
    this.isOpen.set(true);
  }

  private close(): void {
    this.isOpen.set(false);
    this.triggerRef().nativeElement.focus();
  }

  private choose(index: number): void {
    const item = this.items()[index];
    if (!item || this.isItemDisabled(index)) {
      return;
    }
    if (item.kind === 'create') {
      this.close();
      this.createRequested.emit();
      return;
    }
    if (this.isOpen()) {
      this.close();
    }
    if (item.value !== this.value()) {
      this.value.set(item.value);
      this.onChange(item.value);
    }
  }

  private moveHighlight(step: 1 | -1): void {
    const count = this.items().length;
    if (count === 0) {
      return;
    }
    const start =
      this.highlightedIndex() < 0 ? (step === 1 ? 0 : count - 1) : this.highlightedIndex() + step;
    const next = this.firstEnabled(Math.max(0, Math.min(start, count - 1)), step);
    if (next >= 0) {
      this.highlightedIndex.set(next);
    }
  }

  /** First non-disabled row from `start` in the `step` direction; -1 when there is none. */
  private firstEnabled(start: number, step: 1 | -1): number {
    for (let index = start; index >= 0 && index < this.items().length; index += step) {
      if (!this.isItemDisabled(index)) {
        return index;
      }
    }
    return -1;
  }

  private isTypeaheadKey(event: KeyboardEvent): boolean {
    return (
      event.key.length === 1 &&
      event.key !== ' ' &&
      !event.ctrlKey &&
      !event.metaKey &&
      !event.altKey
    );
  }

  /** Accumulates typed characters and returns the first enabled option whose label starts with them. */
  private findByTypeahead(char: string): number {
    if (this.typeaheadTimer) {
      clearTimeout(this.typeaheadTimer);
    }
    this.typeahead += char.toLowerCase();
    this.typeaheadTimer = setTimeout(() => (this.typeahead = ''), TYPEAHEAD_RESET_MS);

    const items = this.items();
    return items.findIndex(
      (item, index) =>
        item.kind === 'option' &&
        !this.isItemDisabled(index) &&
        item.label.toLowerCase().startsWith(this.typeahead),
    );
  }
}
