import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule, Validators } from '@angular/forms';
import { FieldComponent } from '../field/field.component';
import { SelectOptionDirective } from './select-option.directive';
import { SelectComponent } from './select.component';

@Component({
  standalone: true,
  imports: [SelectComponent, SelectOptionDirective, FieldComponent, ReactiveFormsModule],
  template: `
    <app-field label="Тип" [required]="true" [error]="error()">
      <app-select [formControl]="control" placeholder="— выберите тип —" [createLabel]="createLabel()" (createRequested)="createRequests = createRequests + 1">
        @for (item of items(); track item.id) {
          <app-select-option [value]="item.id" [label]="item.name" [disabled]="item.disabled ?? false" />
        }
      </app-select>
    </app-field>
  `,
})
class HostComponent {
  readonly items = signal<{ id: string; name: string; disabled?: boolean }[]>([
    { id: 'a', name: 'Наличные' },
    { id: 'b', name: 'Карта', disabled: true },
    { id: 'c', name: 'Вклад' },
  ]);
  readonly createLabel = signal<string | null>('+ добавить новый');
  readonly error = signal<string | null>(null);
  readonly control = new FormControl<string>('', { nonNullable: true, validators: Validators.required });
  createRequests = 0;
}

describe('SelectComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const trigger = () => root().querySelector('button[role="combobox"]') as HTMLButtonElement;
  const rows = () => Array.from(root().querySelectorAll('[role="option"]')) as HTMLElement[];
  const rowTexts = () => rows().map((row) => row.textContent?.trim());

  function press(key: string, init: KeyboardEventInit = {}): KeyboardEvent {
    const event = new KeyboardEvent('keydown', { key, cancelable: true, bubbles: true, ...init });
    trigger().dispatchEvent(event);
    fixture.detectChanges();
    return event;
  }

  it('shows the placeholder and is labelled by the field (label for = trigger id, aria-required)', () => {
    expect(trigger().textContent?.trim()).toBe('— выберите тип —');
    const label = root().querySelector('label') as HTMLLabelElement;
    expect(label.htmlFor).toBe(trigger().id);
    expect(trigger().getAttribute('aria-required')).toBe('true');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(rows().length).toBe(0);
  });

  it('opens on click with the options in order and the "+ добавить новый" row last', () => {
    trigger().click();
    fixture.detectChanges();

    expect(trigger().getAttribute('aria-expanded')).toBe('true');
    expect(rowTexts()).toEqual(['Наличные', 'Карта', 'Вклад', '+ добавить новый']);
    expect(rows()[3].classList).toContain('app-listbox__option--create');
    expect(rows()[1].getAttribute('aria-disabled')).toBe('true');
    expect(trigger().getAttribute('aria-controls')).toBe(root().querySelector('[role="listbox"]')?.id);
  });

  it('choosing an option writes its value to the control, shows its label and closes the list', () => {
    trigger().click();
    fixture.detectChanges();
    rows()[2].click();
    fixture.detectChanges();

    expect(host.control.value).toBe('c');
    expect(trigger().textContent?.trim()).toBe('Вклад');
    expect(rows().length).toBe(0);
    expect(document.activeElement).toBe(trigger());
  });

  it('a disabled option can not be chosen', () => {
    trigger().click();
    fixture.detectChanges();
    rows()[1].click();
    fixture.detectChanges();

    expect(host.control.value).toBe('');
  });

  it('"+ добавить новый" emits createRequested, keeps the value and closes the list', () => {
    host.control.setValue('a');
    trigger().click();
    fixture.detectChanges();
    rows()[3].click();
    fixture.detectChanges();

    expect(host.createRequests).toBe(1);
    expect(host.control.value).toBe('a');
    expect(rows().length).toBe(0);
  });

  it('has no add row without createLabel', () => {
    host.createLabel.set(null);
    fixture.detectChanges();
    trigger().click();
    fixture.detectChanges();

    expect(rowTexts()).toEqual(['Наличные', 'Карта', 'Вклад']);
  });

  it('keyboard: ArrowDown opens, arrows skip disabled options, Enter chooses; aria-activedescendant follows', () => {
    expect(press('ArrowDown').defaultPrevented).toBe(true);
    expect(trigger().getAttribute('aria-activedescendant')).toBe(rows()[0].id);

    press('ArrowDown');
    expect(trigger().getAttribute('aria-activedescendant')).toBe(rows()[2].id); // "Карта" is disabled

    press('Enter');
    expect(host.control.value).toBe('c');
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
  });

  it('keyboard: End goes to the add row and Enter on it requests a new entry', () => {
    press('Enter');
    press('End');
    expect(trigger().getAttribute('aria-activedescendant')).toBe(rows()[3].id);
    press('Enter');

    expect(host.createRequests).toBe(1);
  });

  it('keyboard: Escape closes without choosing and does not let the event reach a surrounding dialog', () => {
    press('ArrowDown');
    const event = press('Escape');

    expect(event.defaultPrevented).toBe(true);
    expect(trigger().getAttribute('aria-expanded')).toBe('false');
    expect(host.control.value).toBe('');
  });

  it('typing a letter on the closed control selects the first matching option, like a native select', () => {
    press('в');
    expect(host.control.value).toBe('c');
  });

  it('opens on the selected option', () => {
    host.control.setValue('c');
    fixture.detectChanges();
    press('ArrowDown');

    expect(trigger().getAttribute('aria-activedescendant')).toBe(rows()[2].id);
    expect(rows()[2].getAttribute('aria-selected')).toBe('true');
  });

  it('shows the placeholder for a value that is not (yet) among the options, and its label once it appears', () => {
    host.control.setValue('d');
    fixture.detectChanges();
    expect(trigger().textContent?.trim()).toBe('— выберите тип —');

    host.items.update((items) => [...items, { id: 'd', name: 'Кредитка' }]);
    fixture.detectChanges();
    expect(trigger().textContent?.trim()).toBe('Кредитка');
  });

  it('follows the disabled state of the form control', () => {
    host.control.disable();
    fixture.detectChanges();
    expect(trigger().disabled).toBe(true);
  });

  it('marks the control touched when focus leaves the trigger, and reflects a field error in aria-invalid', () => {
    trigger().dispatchEvent(new FocusEvent('blur'));
    expect(host.control.touched).toBe(true);

    host.error.set('Выберите тип');
    fixture.detectChanges();
    expect(trigger().getAttribute('aria-invalid')).toBe('true');
  });
});
