import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { CurrencyOption } from '../currency-option';
import { CurrencySelectComponent } from './currency-select.component';

const options: CurrencyOption[] = [
  { code: 'EUR', name: 'Euro' },
  { code: 'USD', name: 'US Dollar' },
  { code: 'JPY', name: 'Japanese Yen' },
  { code: 'GBP', name: 'Pound Sterling' },
];

@Component({
  standalone: true,
  imports: [CurrencySelectComponent, ReactiveFormsModule],
  template: `
    <app-currency-select
      [currencies]="currencies()"
      [emptyListText]="emptyText"
      ariaLabel="Валюта кошелька"
      [formControl]="control"
    />
  `,
})
class HostComponent {
  readonly currencies = signal<readonly CurrencyOption[]>(options);
  readonly control = new FormControl<string | null>(null);
  emptyText = 'Нет доступных валют';
}

describe('CurrencySelectComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const input = () => root().querySelector('input') as HTMLInputElement;
  const listbox = () => root().querySelector('[role="listbox"]') as HTMLElement | null;
  const optionTexts = () =>
    Array.from(root().querySelectorAll('[role="option"]')).map((el) => el.textContent?.replace(/\s+/g, ' ').trim());

  function open(): void {
    input().focus();
    input().dispatchEvent(new Event('focus'));
    fixture.detectChanges();
  }

  function type(value: string): void {
    input().value = value;
    input().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function press(key: string): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, cancelable: true, bubbles: true });
    input().dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('is a closed combobox with an accessible name until focused', () => {
    expect(input().getAttribute('role')).toBe('combobox');
    expect(input().getAttribute('aria-label')).toBe('Валюта кошелька');
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(input().hasAttribute('aria-controls')).toBe(false);
    expect(listbox()).toBeNull();
  });

  it('opens on focus and lists every option as "code — name"', () => {
    open();

    expect(input().getAttribute('aria-expanded')).toBe('true');
    expect(optionTexts()).toEqual(['EUR — Euro', 'USD — US Dollar', 'JPY — Japanese Yen', 'GBP — Pound Sterling']);
  });

  it('points aria-controls at the listbox and aria-activedescendant at the highlighted option', () => {
    open();

    const popup = listbox()!;
    expect(input().getAttribute('aria-controls')).toBe(popup.id);
    const firstOption = popup.querySelector('[role="option"]') as HTMLElement;
    expect(input().getAttribute('aria-activedescendant')).toBe(firstOption.id);
    expect(firstOption.getAttribute('aria-selected')).toBe('true');
    expect(popup.getAttribute('aria-label')).toBe('Валюта кошелька');
  });

  it('gives each combobox instance its own listbox id', () => {
    const second = TestBed.createComponent(HostComponent);
    second.detectChanges();
    const secondInput = second.nativeElement.querySelector('input') as HTMLInputElement;
    secondInput.dispatchEvent(new Event('focus'));
    second.detectChanges();
    open();

    expect(second.nativeElement.querySelector('[role="listbox"]').id).not.toBe(listbox()!.id);
  });

  it('filters by code, case-insensitively', () => {
    open();
    type('usd');

    expect(optionTexts()).toEqual(['USD — US Dollar']);
  });

  it('filters by name as a substring, case-insensitively', () => {
    open();
    type('dollar');

    expect(optionTexts()).toEqual(['USD — US Dollar']);
  });

  it('ignores surrounding whitespace in the query', () => {
    open();
    type('  yen ');

    expect(optionTexts()).toEqual(['JPY — Japanese Yen']);
  });

  it('shows "Ничего не найдено" (not the empty-list text) when the filter matches nothing', () => {
    open();
    type('zzz');

    expect(optionTexts()).toEqual([]);
    expect(listbox()!.textContent).toContain('Ничего не найдено');
    expect(listbox()!.textContent).not.toContain('Нет доступных валют');
    expect(input().hasAttribute('aria-activedescendant')).toBe(false);
  });

  it('shows emptyListText when the caller supplies no options at all', () => {
    host.currencies.set([]);
    fixture.detectChanges();
    open();

    expect(listbox()!.textContent).toContain('Нет доступных валют');
    expect(listbox()!.textContent).not.toContain('Ничего не найдено');
    expect(root().querySelectorAll('[role="option"]').length).toBe(0);
  });

  it('ArrowDown/ArrowUp move the highlight and aria-activedescendant follows it', () => {
    open();
    const ids = Array.from(root().querySelectorAll('[role="option"]')).map((el) => el.id);

    expect(press('ArrowDown').defaultPrevented).toBe(true);
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[1]);
    press('ArrowDown');
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[2]);
    press('ArrowUp');
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[1]);
    expect(root().querySelectorAll('[aria-selected="true"]').length).toBe(1);
  });

  it('the highlight stays within the list bounds', () => {
    open();
    const ids = Array.from(root().querySelectorAll('[role="option"]')).map((el) => el.id);

    press('ArrowUp');
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[0]);
    for (let i = 0; i < 10; i++) {
      press('ArrowDown');
    }
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[ids.length - 1]);
  });

  it('Enter selects the highlighted option, writes its code to the form control and closes the popup', () => {
    open();
    press('ArrowDown');
    const event = press('Enter');

    expect(event.defaultPrevented).toBe(true);
    expect(host.control.value).toBe('USD');
    expect(listbox()).toBeNull();
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(input().value).toBe('USD — US Dollar');
  });

  it('Enter picks from the filtered list, not from the full one', () => {
    open();
    type('gbp');
    press('Enter');

    expect(host.control.value).toBe('GBP');
  });

  it('Enter with an empty list selects nothing and leaves the control untouched', () => {
    host.currencies.set([]);
    fixture.detectChanges();
    open();
    press('Enter');

    expect(host.control.value).toBeNull();
  });

  it('Escape closes the popup without changing the value', () => {
    host.control.setValue('EUR');
    fixture.detectChanges();
    open();
    type('usd');
    const event = press('Escape');

    expect(event.defaultPrevented).toBe(true);
    expect(listbox()).toBeNull();
    expect(host.control.value).toBe('EUR');
    expect(input().value).toBe('EUR — Euro');
  });

  it('ArrowDown on a closed combobox opens it', () => {
    open();
    press('Escape');
    expect(listbox()).toBeNull();

    const event = press('ArrowDown');

    expect(event.defaultPrevented).toBe(true);
    expect(listbox()).not.toBeNull();
  });

  it('a mouse selection on an option writes its code and marks the control touched', () => {
    open();
    expect(host.control.touched).toBe(false);

    const usd = root().querySelectorAll('[role="option"]')[1] as HTMLElement;
    usd.dispatchEvent(new MouseEvent('mousedown', { bubbles: true }));
    fixture.detectChanges();

    expect(host.control.value).toBe('USD');
    expect(host.control.touched).toBe(true);
    expect(listbox()).toBeNull();
  });

  it('a click outside closes an open popup', () => {
    open();
    document.body.click();
    fixture.detectChanges();

    expect(listbox()).toBeNull();
  });

  it('shows the value written by the form (writeValue) as "code — name"', () => {
    host.control.setValue('JPY');
    fixture.detectChanges();

    expect(input().value).toBe('JPY — Japanese Yen');
  });

  it('shows an empty value when the form is reset to null', () => {
    host.control.setValue('JPY');
    host.control.setValue(null);
    fixture.detectChanges();

    expect(input().value).toBe('');
  });

  it('reflects the disabled state of the form control', () => {
    host.control.disable();
    fixture.detectChanges();

    expect(input().disabled).toBe(true);

    host.control.enable();
    fixture.detectChanges();
    expect(input().disabled).toBe(false);
  });

  it('keeps the selected code when the option list changes and shows its name once it is available', () => {
    host.currencies.set([]);
    host.control.setValue('EUR');
    fixture.detectChanges();
    expect(input().value).toBe('');

    host.currencies.set(options);
    fixture.detectChanges();

    expect(host.control.value).toBe('EUR');
    expect(input().value).toBe('EUR — Euro');
  });
});
