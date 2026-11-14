import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { FieldComponent } from '../../ui/field/field.component';
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

@Component({
  standalone: true,
  imports: [CurrencySelectComponent, FieldComponent, ReactiveFormsModule],
  template: `
    <app-field label="Валюта" [required]="true" [hint]="hint()" [error]="error()">
      <app-currency-select [currencies]="currencies" [formControl]="control" />
    </app-field>
  `,
})
class FieldHostComponent {
  readonly currencies = options;
  readonly control = new FormControl<string | null>(null, Validators.required);
  readonly hint = signal<string | null>(null);
  readonly error = signal<string | null>(null);
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
  const liveRegion = () => root().querySelector('[aria-live="polite"]') as HTMLElement;
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
    expect(liveRegion().textContent).toContain('Ничего не найдено');
    expect(liveRegion().textContent).not.toContain('Нет доступных валют');
    expect(input().hasAttribute('aria-activedescendant')).toBe(false);
  });

  it('shows emptyListText when the caller supplies no options at all', () => {
    host.currencies.set([]);
    fixture.detectChanges();
    open();

    expect(liveRegion().textContent).toContain('Нет доступных валют');
    expect(liveRegion().textContent).not.toContain('Ничего не найдено');
    expect(root().querySelectorAll('[role="option"]').length).toBe(0);
  });

  it('keeps the empty/nothing-found message out of the listbox, in a polite live region that is always in the DOM', () => {
    const region = liveRegion();
    expect(region.getAttribute('aria-live')).toBe('polite');
    expect(region.hidden).toBe(true);

    host.currencies.set([]);
    fixture.detectChanges();
    open();

    expect(liveRegion()).toBe(region);
    expect(region.hidden).toBe(false);
    // No listbox (and no role="presentation" children) when there is nothing to pick; aria-expanded follows the visible list.
    expect(listbox()).toBeNull();
    expect(input().getAttribute('aria-expanded')).toBe('false');
    expect(input().hasAttribute('aria-controls')).toBe(false);
    expect(root().querySelector('[role="presentation"]')).toBeNull();
  });

  it('hides the live region again when the popup closes', () => {
    host.currencies.set([]);
    fixture.detectChanges();
    open();
    press('Escape');

    expect(liveRegion().hidden).toBe(true);
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

  it('has no aria-activedescendant while the popup is closed', () => {
    open();
    expect(input().hasAttribute('aria-activedescendant')).toBe(true);

    press('Escape');

    expect(input().hasAttribute('aria-activedescendant')).toBe(false);
  });

  it('Home and End jump to the first and last option', () => {
    open();
    const ids = Array.from(root().querySelectorAll('[role="option"]')).map((el) => el.id);

    expect(press('End').defaultPrevented).toBe(true);
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[ids.length - 1]);
    expect(press('Home').defaultPrevented).toBe(true);
    expect(input().getAttribute('aria-activedescendant')).toBe(ids[0]);
  });

  it('Home and End are left to the caret when there is no option to highlight', () => {
    open();
    type('zzz');

    expect(press('Home').defaultPrevented).toBe(false);
    expect(press('End').defaultPrevented).toBe(false);
  });

  it('blur closes the popup, marks the control touched and keeps the selected value visible', () => {
    host.control.setValue('EUR');
    fixture.detectChanges();
    open();
    expect(host.control.touched).toBe(false);

    input().dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(listbox()).toBeNull();
    expect(host.control.touched).toBe(true);
    expect(host.control.value).toBe('EUR');
    expect(input().value).toBe('EUR — Euro');
  });

  it('blur discards unfinished search text without changing the value', () => {
    host.control.setValue('EUR');
    fixture.detectChanges();
    open();
    type('usd');

    input().dispatchEvent(new Event('blur'));
    fixture.detectChanges();

    expect(host.control.value).toBe('EUR');
    expect(input().value).toBe('EUR — Euro');
  });

  it('clicking the already focused input re-opens a popup closed with Escape', () => {
    open();
    press('Escape');
    expect(listbox()).toBeNull();

    input().click();
    fixture.detectChanges();

    expect(listbox()).not.toBeNull();
  });

  it('clicking an input whose popup is already open leaves it (and the highlight) alone', () => {
    open();
    press('ArrowDown');

    input().click();
    fixture.detectChanges();

    expect(input().getAttribute('aria-activedescendant')).toBe(root().querySelectorAll('[role="option"]')[1].id);
  });

  it('keeps focus in the input on mousedown inside the popup (so blur does not close it before the pick)', () => {
    open();

    const optionEvent = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    (root().querySelectorAll('[role="option"]')[1] as HTMLElement).dispatchEvent(optionEvent);
    expect(optionEvent.defaultPrevented).toBe(true);

    open();
    const listEvent = new MouseEvent('mousedown', { bubbles: true, cancelable: true });
    listbox()!.dispatchEvent(listEvent);
    expect(listEvent.defaultPrevented).toBe(true);
  });

  it('focus() moves keyboard focus into the input without opening the popup; a later click or ArrowDown opens it', () => {
    document.body.appendChild(root());
    fixture.debugElement.query((d) => d.name === 'app-currency-select').componentInstance.focus();
    fixture.detectChanges();

    expect(document.activeElement).toBe(input());
    expect(listbox()).toBeNull();

    press('ArrowDown');
    expect(listbox()).not.toBeNull();
    root().remove();
  });

  it('a regular focus after a programmatic focus() still opens the popup', () => {
    document.body.appendChild(root());
    const select = fixture.debugElement.query((d) => d.name === 'app-currency-select').componentInstance;
    select.focus();
    input().blur();

    input().focus();
    fixture.detectChanges();

    expect(listbox()).not.toBeNull();
    root().remove();
  });

  it('has no id and no field-derived ARIA state outside an app-field', () => {
    expect(input().hasAttribute('id')).toBe(false);
    expect(input().hasAttribute('aria-describedby')).toBe(false);
    expect(input().hasAttribute('aria-invalid')).toBe(false);
    expect(input().hasAttribute('aria-required')).toBe(false);
    expect(root().querySelector('app-currency-select')!.hasAttribute('id')).toBe(false);
  });

  describe('inside an app-field', () => {
    let fieldFixture: ComponentFixture<FieldHostComponent>;
    const fieldRoot = () => fieldFixture.nativeElement as HTMLElement;
    const fieldInput = () => fieldRoot().querySelector('app-currency-select input') as HTMLInputElement;

    beforeEach(() => {
      fieldFixture = TestBed.createComponent(FieldHostComponent);
      fieldFixture.detectChanges();
    });

    it('takes its id from the field, so the field label targets the inner input', () => {
      const label = fieldRoot().querySelector('label') as HTMLLabelElement;

      expect(fieldInput().id).toBeTruthy();
      expect(label.htmlFor).toBe(fieldInput().id);
      expect(fieldRoot().querySelector('app-currency-select')!.hasAttribute('id')).toBe(false);
    });

    it('is aria-required when the field is required', () => {
      expect(fieldInput().getAttribute('aria-required')).toBe('true');
    });

    it('is described by the field hint, and by the field error (aria-invalid) when there is one', () => {
      expect(fieldInput().hasAttribute('aria-describedby')).toBe(false);
      expect(fieldInput().hasAttribute('aria-invalid')).toBe(false);

      fieldFixture.componentInstance.hint.set('Подсказка');
      fieldFixture.detectChanges();
      const hint = fieldRoot().querySelector('.app-field__hint') as HTMLElement;
      expect(fieldInput().getAttribute('aria-describedby')).toBe(hint.id);

      fieldFixture.componentInstance.error.set('Ошибка');
      fieldFixture.detectChanges();
      const error = fieldRoot().querySelector('.app-field__error') as HTMLElement;
      expect(fieldInput().getAttribute('aria-describedby')).toBe(error.id);
      expect(fieldInput().getAttribute('aria-invalid')).toBe('true');
    });
  });
});
