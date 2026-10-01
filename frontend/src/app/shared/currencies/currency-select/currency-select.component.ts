import { ChangeDetectionStrategy, Component, computed, ElementRef, forwardRef, HostListener, inject, input, signal, viewChild } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { FieldComponent } from '../../ui/field/field.component';
import { CurrencyOption } from '../currency-option';

let nextId = 0;

/**
 * Searchable currency picker (ARIA combobox pattern) — a plain <select> with
 * 150+ currencies has no way to filter as you type, so finding one meant
 * scanning the whole list. Filters by code or name (case-insensitive
 * substring), keyboard-navigable (arrows, Home/End, Enter, Escape), usable via
 * formControlName like any other ControlValueAccessor. The option list is
 * supplied by the caller (see `CurrencyOption`); when it is empty the popup
 * shows `emptyListText` instead of a bare "nothing found".
 *
 * Inside an `app-field` the inner `<input>` takes its id (so the field's
 * `<label for>` reaches it), `aria-describedby`, `aria-invalid` and
 * `aria-required` from the field, exactly like `FieldControlDirective` does for
 * native controls — no manual id is needed. Outside a field, use `ariaLabel`.
 *
 * The popup closes (and the control is marked touched) when the input loses
 * focus; the selected value stays visible. Pressing the focused input again
 * re-opens a closed popup.
 */
@Component({
  selector: 'app-currency-select',
  standalone: true,
  templateUrl: './currency-select.component.html',
  styleUrl: './currency-select.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  providers: [
    {
      provide: NG_VALUE_ACCESSOR,
      useExisting: forwardRef(() => CurrencySelectComponent),
      multi: true,
    },
  ],
})
export class CurrencySelectComponent implements ControlValueAccessor {
  private readonly host = inject(ElementRef<HTMLElement>);
  /** The surrounding `app-field`, if any — source of the input's id and aria-* state. */
  protected readonly field = inject(FieldComponent, { optional: true });
  private readonly inputElement = viewChild.required<ElementRef<HTMLInputElement>>('inputElement');

  readonly currencies = input.required<readonly CurrencyOption[]>();
  readonly placeholder = input('— выберите валюту —');
  /** Announced politely when the caller supplies no options at all (as opposed to a search that matched nothing). */
  readonly emptyListText = input('Список валют пуст');
  /** Accessible name for the input and the popup when no `app-field` label points at the input. */
  readonly ariaLabel = input<string | null>(null);

  readonly listboxId = `currency-select-listbox-${nextId++}`;

  readonly isOpen = signal(false);
  readonly searchText = signal('');
  readonly highlightedIndex = signal(0);
  readonly disabled = signal(false);

  private readonly selectedCode = signal<string | null>(null);
  /** Cleared only for the synchronous focus event raised by `focus()`. */
  private openOnFocus = true;
  private onChange: (value: string | null) => void = () => {};
  private onTouched: () => void = () => {};

  readonly selectedCurrency = computed(() => this.currencies().find((c) => c.code === this.selectedCode()) ?? null);

  readonly filteredCurrencies = computed(() => {
    const query = this.searchText().trim().toLowerCase();
    const all = this.currencies();
    if (!query) {
      return all;
    }
    return all.filter(
      (currency) => currency.code.toLowerCase().includes(query) || currency.name.toLowerCase().includes(query),
    );
  });

  readonly inputValue = computed(() => {
    if (this.isOpen()) {
      return this.searchText();
    }
    const selected = this.selectedCurrency();
    return selected ? `${selected.code} — ${selected.name}` : '';
  });

  /** The option list is rendered (and `aria-expanded`/`aria-controls` apply) only while open and non-empty. */
  readonly listVisible = computed(() => this.isOpen() && this.filteredCurrencies().length > 0);

  /** Message for the polite live region while open and there is nothing to pick; `null` otherwise. */
  readonly emptyMessage = computed(() => {
    if (!this.isOpen()) {
      return null;
    }
    if (this.currencies().length === 0) {
      return this.emptyListText();
    }
    return this.filteredCurrencies().length === 0 ? 'Ничего не найдено' : null;
  });

  /** Only meaningful while the popup is open — a stale reference to a removed option is invalid ARIA. */
  readonly activeOptionId = computed(() => {
    if (!this.listVisible()) {
      return null;
    }
    const index = this.highlightedIndex();
    return this.filteredCurrencies()[index] ? `${this.listboxId}-option-${index}` : null;
  });

  writeValue(value: string | null): void {
    this.selectedCode.set(value);
  }

  registerOnChange(fn: (value: string | null) => void): void {
    this.onChange = fn;
  }

  registerOnTouched(fn: () => void): void {
    this.onTouched = fn;
  }

  setDisabledState(isDisabled: boolean): void {
    this.disabled.set(isDisabled);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: MouseEvent): void {
    if (this.isOpen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  /**
   * Moves keyboard focus into the input without opening the popup (e.g. to restore focus after a retry —
   * a popup popping open on its own would be a surprise there). Arrow keys, Enter or a click open it.
   */
  focus(): void {
    this.openOnFocus = false;
    try {
      this.inputElement().nativeElement.focus();
    } finally {
      this.openOnFocus = true;
    }
  }

  onFocus(): void {
    if (this.openOnFocus) {
      this.open();
    }
  }

  /** Pressing the already focused input re-opens a popup closed with Escape or a selection (no new focus event fires). */
  onClick(): void {
    if (!this.isOpen()) {
      this.open();
    }
  }

  /** Focus left the input: close the popup (the selected value stays shown) and mark the control touched. */
  onBlur(): void {
    this.close();
  }

  onInput(value: string): void {
    this.searchText.set(value);
    this.highlightedIndex.set(0);
    if (!this.isOpen()) {
      this.isOpen.set(true);
    }
  }

  onKeydown(event: KeyboardEvent): void {
    if (!this.isOpen() && (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Enter')) {
      this.isOpen.set(true);
      event.preventDefault();
      return;
    }
    if (!this.isOpen()) {
      return;
    }

    const optionCount = this.filteredCurrencies().length;
    switch (event.key) {
      case 'ArrowDown':
        event.preventDefault();
        this.highlightedIndex.update((index) => Math.max(Math.min(index + 1, optionCount - 1), 0));
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.highlightedIndex.update((index) => Math.max(index - 1, 0));
        break;
      case 'Enter':
        event.preventDefault();
        this.selectAt(this.highlightedIndex());
        break;
      case 'Home':
        if (optionCount > 0) {
          event.preventDefault();
          this.highlightedIndex.set(0);
        }
        break;
      case 'End':
        if (optionCount > 0) {
          event.preventDefault();
          this.highlightedIndex.set(optionCount - 1);
        }
        break;
      case 'Escape':
        event.preventDefault();
        this.close();
        break;
    }
  }

  selectAt(index: number): void {
    const currency = this.filteredCurrencies()[index];
    if (!currency) {
      return;
    }
    this.selectedCode.set(currency.code);
    this.onChange(currency.code);
    this.close();
  }

  private open(): void {
    this.searchText.set('');
    this.highlightedIndex.set(0);
    this.isOpen.set(true);
  }

  private close(): void {
    this.isOpen.set(false);
    this.searchText.set('');
    this.onTouched();
  }
}
