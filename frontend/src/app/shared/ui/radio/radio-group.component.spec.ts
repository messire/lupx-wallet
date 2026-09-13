import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { RadioGroupComponent } from './radio-group.component';
import { RadioOptionComponent } from './radio-option.component';

describe('RadioGroupComponent (ControlValueAccessor contract)', () => {
  let fixture: ComponentFixture<RadioGroupComponent>;
  let component: RadioGroupComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [RadioGroupComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(RadioGroupComponent);
    component = fixture.componentInstance;
    fixture.detectChanges();
  });

  it('writeValue updates the current value', () => {
    component.writeValue('b');

    expect(component.value()).toBe('b');
  });

  it('writeValue(null) clears the current value', () => {
    component.writeValue('a');
    component.writeValue(null);

    expect(component.value()).toBeNull();
  });

  it('select() invokes registerOnChange and registerOnTouched with the new value', () => {
    const onChange = vi.fn();
    const onTouched = vi.fn();
    component.registerOnChange(onChange);
    component.registerOnTouched(onTouched);

    component.select('b');

    expect(component.value()).toBe('b');
    expect(onChange).toHaveBeenCalledWith('b');
    expect(onTouched).toHaveBeenCalledTimes(1);
  });

  it('setDisabledState updates the disabled signal', () => {
    component.setDisabledState(true);

    expect(component.disabled()).toBe(true);
  });
});

@Component({
  standalone: true,
  imports: [RadioGroupComponent, RadioOptionComponent, FormsModule],
  template: `
    <app-radio-group [(ngModel)]="selected">
      <app-radio-option value="cash" label="Наличные" />
      <app-radio-option value="card" label="Карта" />
      <app-radio-option value="crypto" label="Крипто" />
    </app-radio-group>
  `,
})
class NgModelHostComponent {
  selected: string | null = 'cash';
}

describe('RadioGroupComponent with app-radio-option children and [(ngModel)]', () => {
  let fixture: ComponentFixture<NgModelHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NgModelHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(NgModelHostComponent);
    fixture.detectChanges();
    // NgModel pushes model -> view updates through a resolved promise (see
    // @angular/forms NgModel#_updateValue) to avoid ExpressionChangedAfterItHasBeenChecked;
    // a microtask must be flushed before the initial value reaches the CVA.
    await fixture.whenStable();
    fixture.detectChanges();
  });

  function radioInputs(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('input[type="radio"]'));
  }

  it('checks the option matching the initial model value', () => {
    const inputs = radioInputs();

    expect(inputs[0].checked).toBe(true);
    expect(inputs[1].checked).toBe(false);
    expect(inputs[2].checked).toBe(false);
  });

  it('shares a single name attribute across all options in the group so only one can be selected', () => {
    const inputs = radioInputs();

    expect(inputs[0].name).toBe(inputs[1].name);
    expect(inputs[1].name).toBe(inputs[2].name);
  });

  it('selecting a different option updates the model and unchecks the previous option', () => {
    const inputs = radioInputs();

    inputs[2].checked = true;
    inputs[2].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.selected).toBe('crypto');
    expect(inputs[0].checked).toBe(false);
    expect(inputs[2].checked).toBe(true);
  });
});

@Component({
  standalone: true,
  imports: [RadioGroupComponent, RadioOptionComponent, ReactiveFormsModule],
  template: `
    <app-radio-group [formControl]="control">
      <app-radio-option value="cash" label="Наличные" />
      <app-radio-option value="card" label="Карта" />
    </app-radio-group>
  `,
})
class ReactiveFormHostComponent {
  control = new FormControl<string | null>('cash');
}

describe('RadioGroupComponent with formControl', () => {
  let fixture: ComponentFixture<ReactiveFormHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReactiveFormHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ReactiveFormHostComponent);
    fixture.detectChanges();
  });

  function radioInputs(): HTMLInputElement[] {
    return Array.from(fixture.nativeElement.querySelectorAll('input[type="radio"]'));
  }

  it('disabling the FormControl disables every rendered option', () => {
    fixture.componentInstance.control.disable();
    fixture.detectChanges();

    const inputs = radioInputs();
    expect(inputs[0].disabled).toBe(true);
    expect(inputs[1].disabled).toBe(true);
  });

  it('selecting an option updates the FormControl value', () => {
    const inputs = radioInputs();

    inputs[1].checked = true;
    inputs[1].dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.control.value).toBe('card');
  });
});
