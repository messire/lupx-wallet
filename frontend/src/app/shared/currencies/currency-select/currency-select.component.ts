import { ChangeDetectionStrategy, Component, computed, ElementRef, forwardRef, HostListener, inject, input, signal } from '@angular/core';
import { ControlValueAccessor, NG_VALUE_ACCESSOR } from '@angular/forms';
import { IsoCurrency } from '../iso-currencies';

let nextId = 0;

/**
 * Searchable currency picker (ARIA combobox pattern) — a plain <select> with
 * 150+ ISO 4217 currencies has no way to filter as you type, so finding one
 * meant scanning the whole list. Filters by code or name (case-insensitive
 * substring), keyboard-navigable, usable via formControlName like any other
 * ControlValueAccessor.
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

  readonly currencies = input.required<readonly IsoCurrency[]>();
  readonly placeholder = input('— выберите валюту —');
  /** Lets an external `<label for>` (e.g. `app-field`'s `controlId()`) target the inner `<input>`. */
  readonly id = input<string | null>(null);

  readonly listboxId = `currency-select-listbox-${nextId++}`;

  readonly isOpen = signal(false);
  readonly searchText = signal('');
  readonly highlightedIndex = signal(0);
  readonly disabled = signal(false);

  private readonly selectedCode = signal<string | null>(null);
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

  readonly activeOptionId = computed(() => {
    const options = this.filteredCurrencies();
    const index = this.highlightedIndex();
    return options[index] ? `${this.listboxId}-option-${index}` : null;
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

  onFocus(): void {
    this.searchText.set('');
    this.highlightedIndex.set(0);
    this.isOpen.set(true);
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
        this.highlightedIndex.update((index) => Math.min(index + 1, optionCount - 1));
        break;
      case 'ArrowUp':
        event.preventDefault();
        this.highlightedIndex.update((index) => Math.max(index - 1, 0));
        break;
      case 'Enter':
        event.preventDefault();
        this.selectAt(this.highlightedIndex());
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

  private close(): void {
    this.isOpen.set(false);
    this.searchText.set('');
    this.onTouched();
  }
}
