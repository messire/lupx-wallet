import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, ReactiveFormsModule } from '@angular/forms';
import { FieldComponent } from '../field/field.component';
import { DateInputComponent } from './date-input.component';

@Component({
  standalone: true,
  imports: [DateInputComponent, FieldComponent, ReactiveFormsModule],
  template: `
    <app-field label="Дата" [required]="true">
      <app-date-input [formControl]="control" [min]="min()" [max]="max()" />
    </app-field>
  `,
})
class HostComponent {
  readonly control = new FormControl<string>('2026-11-14', { nonNullable: true });
  readonly min = signal<string | null>(null);
  readonly max = signal<string | null>(null);
}

describe('DateInputComponent', () => {
  let fixture: ComponentFixture<HostComponent>;
  let host: HostComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [HostComponent] }).compileComponents();
    fixture = TestBed.createComponent(HostComponent);
    host = fixture.componentInstance;
    fixture.detectChanges();
  });

  const root = () => fixture.nativeElement as HTMLElement;
  const text = () => root().querySelector('app-date-input input') as HTMLInputElement;
  const toggle = () => root().querySelector('.app-date-input__toggle') as HTMLButtonElement;
  const panel = () => root().querySelector('[role="dialog"]') as HTMLElement | null;
  const day = (iso: string) =>
    Array.from(root().querySelectorAll('.app-date-input__day')).find(
      (button) => button.getAttribute('aria-label')?.startsWith(isoLabelPrefix(iso)) && !button.classList.contains('is-other-month'),
    ) as HTMLButtonElement;

  function isoLabelPrefix(iso: string): string {
    const [, , d] = iso.split('-');
    return `${Number(d)} `;
  }

  function type(value: string): void {
    text().value = value;
    text().dispatchEvent(new Event('input'));
    fixture.detectChanges();
  }

  function openCalendar(): void {
    toggle().click();
    fixture.detectChanges();
  }

  it('shows the value as дд.мм.гггг and is labelled by the field', () => {
    expect(text().value).toBe('14.11.2026');
    expect((root().querySelector('label') as HTMLLabelElement).htmlFor).toBe(text().id);
    expect(text().getAttribute('aria-required')).toBe('true');
  });

  it('typing a date (Russian or ISO form) writes yyyy-MM-dd to the control', () => {
    type('01.02.2026');
    expect(host.control.value).toBe('2026-02-01');
    type('2026-03-05');
    expect(host.control.value).toBe('2026-03-05');
  });

  it('clearing the text empties the value; text that is not a date leaves it empty and invalid', () => {
    type('');
    expect(host.control.value).toBe('');
    expect(host.control.valid).toBe(true);

    type('31.02.2026');
    expect(host.control.value).toBe('');
    expect(host.control.hasError('dateFormat')).toBe(true);
  });

  it('the calendar opens on the selected month, marks the selected day and chooses a clicked day', () => {
    openCalendar();
    expect(panel()).not.toBeNull();
    expect(root().querySelector('.app-date-input__title')?.textContent).toContain('Ноябрь 2026');
    expect(day('2026-11-14').getAttribute('aria-pressed')).toBe('true');
    expect(toggle().getAttribute('aria-expanded')).toBe('true');

    day('2026-11-20').click();
    fixture.detectChanges();

    expect(host.control.value).toBe('2026-11-20');
    expect(text().value).toBe('20.11.2026');
    expect(panel()).toBeNull();
  });

  it('month navigation and keyboard moves change the shown month', () => {
    openCalendar();
    (root().querySelector('[aria-label="Следующий месяц"]') as HTMLButtonElement).click();
    fixture.detectChanges();
    expect(root().querySelector('.app-date-input__title')?.textContent).toContain('Декабрь 2026');

    const grid = root().querySelector('[role="grid"]') as HTMLElement;
    grid.dispatchEvent(new KeyboardEvent('keydown', { key: 'PageUp', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    grid.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true, cancelable: true }));
    fixture.detectChanges();
    grid.dispatchEvent(new KeyboardEvent('keydown', { key: 'Enter', bubbles: true, cancelable: true }));
    fixture.detectChanges();

    expect(host.control.value).toBe('2026-11-15');
  });

  it('days outside min/max are marked and can not be chosen', () => {
    host.min.set('2026-11-10');
    host.max.set('2026-11-20');
    fixture.detectChanges();
    openCalendar();

    expect(day('2026-11-09').getAttribute('aria-disabled')).toBe('true');
    day('2026-11-21').click();
    fixture.detectChanges();
    expect(host.control.value).toBe('2026-11-14');
  });

  it('Escape closes the calendar without leaking to a surrounding dialog', () => {
    openCalendar();
    const event = new KeyboardEvent('keydown', { key: 'Escape', bubbles: true, cancelable: true });
    panel()!.dispatchEvent(event);
    fixture.detectChanges();

    expect(event.defaultPrevented).toBe(true);
    expect(panel()).toBeNull();
  });

  it('follows the disabled state of the form control', () => {
    host.control.disable();
    fixture.detectChanges();
    expect(text().disabled).toBe(true);
    expect(toggle().disabled).toBe(true);
  });
});
