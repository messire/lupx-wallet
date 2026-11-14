import {
  ChangeDetectionStrategy,
  Component,
  ElementRef,
  HostListener,
  Injector,
  afterNextRender,
  computed,
  forwardRef,
  inject,
  input,
  signal,
  viewChild,
} from '@angular/core';
import {
  AbstractControl,
  ControlValueAccessor,
  NG_VALIDATORS,
  NG_VALUE_ACCESSOR,
  ValidationErrors,
  Validator,
} from '@angular/forms';
import { AnchoredPanelDirective } from '../popover/anchored-panel.directive';
import { FieldComponent } from '../field/field.component';
import {
  MONTH_NAMES,
  WEEKDAY_SHORT,
  addDays,
  addMonths,
  describeDate,
  formatDateText,
  monthGrid,
  parseDateText,
  splitIsoDate,
  todayIsoDate,
  weekdayIndex,
} from './calendar-date';

let nextId = 0;

/**
 * Date field in the application theme (ui-kit §5, ADR-0009 §6) — replaces `<input type="date">`, whose calendar
 * is drawn by the browser in its own (system grey) style.
 *
 * The value is the API date string `yyyy-MM-dd`, or `''` when the field is empty — the same contract the native
 * date input had, so forms and validators keep working unchanged. The text box shows and accepts `дд.мм.гггг`
 * (also the ISO form); the calendar button opens a month grid (Monday-first) in an anchored panel.
 *
 * Keyboard in the grid: arrows move by a day/week, Home/End — to the start/end of the week, PageUp/PageDown —
 * by a month (with Shift — by a year), Enter/Space choose, Escape closes and returns focus to the text box.
 * Days outside `min`/`max` are shown but can't be chosen (range checks with their own messages stay in the forms).
 *
 * Text that is not a real date leaves the value `''` and makes the control invalid with `{ dateFormat: true }`.
 * Inside an `app-field` the text box takes id/aria-* from the field, like `FieldControlDirective`.
 */
@Component({
  selector: 'app-date-input',
  standalone: true,
  imports: [AnchoredPanelDirective],
  templateUrl: './date-input.component.html',
  styleUrl: './date-input.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    // The current bounds, readable from outside (end-to-end checks, debugging), as `min`/`max` were on the native input.
    '[attr.data-min]': 'min() || null',
    '[attr.data-max]': 'max() || null',
  },
  providers: [
    { provide: NG_VALUE_ACCESSOR, useExisting: forwardRef(() => DateInputComponent), multi: true },
    { provide: NG_VALIDATORS, useExisting: forwardRef(() => DateInputComponent), multi: true },
  ],
})
export class DateInputComponent implements ControlValueAccessor, Validator {
  protected readonly field = inject(FieldComponent, { optional: true });
  private readonly host = inject<ElementRef<HTMLElement>>(ElementRef);
  private readonly injector = inject(Injector);
  private readonly textInput = viewChild.required<ElementRef<HTMLInputElement>>('textInput');
  private readonly wrapper = viewChild.required<ElementRef<HTMLElement>>('wrapper');
  private readonly grid = viewChild<ElementRef<HTMLElement>>('grid');

  /** Earliest choosable day, `yyyy-MM-dd`. */
  readonly min = input<string | null | undefined>(null);
  /** Latest choosable day, `yyyy-MM-dd`. */
  readonly max = input<string | null | undefined>(null);
  readonly placeholder = input('дд.мм.гггг');
  /** Accessible name when no `app-field` label points at the text box. */
  readonly ariaLabel = input<string | null>(null);

  readonly panelId = `app-date-input-panel-${nextId++}`;
  readonly weekdays = WEEKDAY_SHORT;

  readonly value = signal('');
  readonly text = signal('');
  readonly disabled = signal(false);
  readonly isOpen = signal(false);
  /** The day that holds keyboard focus in the grid; its month is the one shown. */
  readonly activeDate = signal(todayIsoDate());
  readonly today = signal(todayIsoDate());

  readonly weeks = computed(() => monthGrid(this.activeDate()));
  readonly monthTitle = computed(() => {
    const { year, month } = splitIsoDate(this.activeDate());
    return `${MONTH_NAMES[month - 1]} ${year}`;
  });
  readonly wrapperElement = computed(() => this.wrapper().nativeElement);

  private onChange: (value: string) => void = () => {};
  private onTouched: () => void = () => {};
  private onValidatorChange: () => void = () => {};

  // ---- ControlValueAccessor / Validator ----

  writeValue(value: string | null): void {
    const normalized = value ?? '';
    this.value.set(normalized);
    this.text.set(formatDateText(normalized));
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

  validate(_control: AbstractControl): ValidationErrors | null {
    const text = this.text().trim();
    return text && !parseDateText(text) ? { dateFormat: true } : null;
  }

  registerOnValidatorChange(fn: () => void): void {
    this.onValidatorChange = fn;
  }

  // ---- Text box ----

  onTextInput(text: string): void {
    this.text.set(text);
    const parsed = text.trim() ? parseDateText(text) : '';
    this.setValue(parsed ?? '');
    this.onValidatorChange();
  }

  onTextBlur(event: FocusEvent): void {
    const parsed = parseDateText(this.text());
    if (parsed) {
      this.text.set(formatDateText(parsed));
    }
    if (!this.containsFocusTarget(event)) {
      this.onTouched();
    }
  }

  onTextKeydown(event: KeyboardEvent): void {
    if (event.key === 'ArrowDown' && event.altKey) {
      event.preventDefault();
      this.open();
    }
  }

  // ---- Calendar ----

  toggle(): void {
    if (this.isOpen()) {
      this.close(true);
    } else {
      this.open();
    }
  }

  open(): void {
    if (this.disabled()) {
      return;
    }
    this.today.set(todayIsoDate());
    this.activeDate.set(this.clamp(this.value() || this.today()));
    this.isOpen.set(true);
    afterNextRender(() => this.focusActiveDay(), { injector: this.injector });
  }

  close(returnFocus: boolean): void {
    this.isOpen.set(false);
    if (returnFocus) {
      this.textInput().nativeElement.focus();
    }
  }

  showMonth(step: number): void {
    this.activeDate.set(addMonths(this.activeDate(), step));
  }

  choose(isoDate: string): void {
    if (this.isOutOfRange(isoDate)) {
      return;
    }
    this.text.set(formatDateText(isoDate));
    this.setValue(isoDate);
    this.onValidatorChange();
    this.close(true);
  }

  chooseToday(): void {
    this.choose(this.today());
  }

  onGridKeydown(event: KeyboardEvent): void {
    const active = this.activeDate();
    let next: string | null = null;
    switch (event.key) {
      case 'ArrowLeft':
        next = addDays(active, -1);
        break;
      case 'ArrowRight':
        next = addDays(active, 1);
        break;
      case 'ArrowUp':
        next = addDays(active, -7);
        break;
      case 'ArrowDown':
        next = addDays(active, 7);
        break;
      case 'Home':
        next = addDays(active, -weekdayIndex(active));
        break;
      case 'End':
        next = addDays(active, 6 - weekdayIndex(active));
        break;
      case 'PageUp':
        next = addMonths(active, event.shiftKey ? -12 : -1);
        break;
      case 'PageDown':
        next = addMonths(active, event.shiftKey ? 12 : 1);
        break;
      case 'Enter':
      case ' ':
        event.preventDefault();
        this.choose(active);
        return;
      default:
        return;
    }
    event.preventDefault();
    this.activeDate.set(next);
    afterNextRender(() => this.focusActiveDay(), { injector: this.injector });
  }

  onPanelKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape') {
      event.preventDefault();
      event.stopPropagation(); // inside a dialog: close the calendar, not the dialog
      this.close(true);
    }
  }

  onPanelFocusout(event: FocusEvent): void {
    if (!this.containsFocusTarget(event)) {
      this.close(false);
      this.onTouched();
    }
  }

  @HostListener('document:mousedown', ['$event'])
  onDocumentMousedown(event: MouseEvent): void {
    if (this.isOpen() && !this.host.nativeElement.contains(event.target as Node)) {
      this.close(false);
    }
  }

  // ---- View helpers ----

  isOutOfRange(isoDate: string): boolean {
    const min = this.min();
    const max = this.max();
    return (!!min && isoDate < min) || (!!max && isoDate > max);
  }

  isOtherMonth(isoDate: string): boolean {
    return isoDate.slice(0, 7) !== this.activeDate().slice(0, 7);
  }

  dayNumber(isoDate: string): number {
    return splitIsoDate(isoDate).day;
  }

  dayLabel(isoDate: string): string {
    return describeDate(isoDate);
  }

  private setValue(value: string): void {
    if (value !== this.value()) {
      this.value.set(value);
      this.onChange(value);
    }
  }

  private clamp(isoDate: string): string {
    const min = this.min();
    const max = this.max();
    if (min && isoDate < min) {
      return min;
    }
    if (max && isoDate > max) {
      return max;
    }
    return isoDate;
  }

  private focusActiveDay(): void {
    this.grid()?.nativeElement.querySelector<HTMLElement>('[data-active="true"]')?.focus();
  }

  private containsFocusTarget(event: FocusEvent): boolean {
    const next = event.relatedTarget as Node | null;
    return !!next && this.host.nativeElement.contains(next);
  }
}
