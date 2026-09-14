import { Component } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FormControl, FormsModule, ReactiveFormsModule } from '@angular/forms';
import { CheckboxComponent } from './checkbox.component';

describe('CheckboxComponent (ControlValueAccessor contract)', () => {
  let fixture: ComponentFixture<CheckboxComponent>;
  let component: CheckboxComponent;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [CheckboxComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(CheckboxComponent);
    component = fixture.componentInstance;
    fixture.componentRef.setInput('label', 'Согласен с условиями');
    fixture.detectChanges();
  });

  function inputEl(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('writeValue updates the checked state and the rendered input', () => {
    component.writeValue(true);
    fixture.detectChanges();

    expect(component.checked()).toBe(true);
    expect(inputEl().checked).toBe(true);
  });

  it('writeValue(null) is treated as unchecked', () => {
    component.writeValue(true);
    component.writeValue(null);
    fixture.detectChanges();

    expect(component.checked()).toBe(false);
    expect(inputEl().checked).toBe(false);
  });

  it('registerOnChange is invoked with the new value when the input changes', () => {
    const onChange = vi.fn();
    component.registerOnChange(onChange);

    inputEl().checked = true;
    inputEl().dispatchEvent(new Event('change'));

    expect(onChange).toHaveBeenCalledWith(true);
    expect(component.checked()).toBe(true);
  });

  it('registerOnTouched is invoked on blur', () => {
    const onTouched = vi.fn();
    component.registerOnTouched(onTouched);

    inputEl().dispatchEvent(new Event('blur'));

    expect(onTouched).toHaveBeenCalledTimes(1);
  });

  it('setDisabledState disables the rendered input', () => {
    component.setDisabledState(true);
    fixture.detectChanges();

    expect(component.disabled()).toBe(true);
    expect(inputEl().disabled).toBe(true);
  });
});

@Component({
  standalone: true,
  imports: [CheckboxComponent, FormsModule],
  template: `<app-checkbox label="Уведомления" [(ngModel)]="agreed"></app-checkbox>`,
})
class NgModelHostComponent {
  agreed = false;
}

describe('CheckboxComponent with [(ngModel)]', () => {
  let fixture: ComponentFixture<NgModelHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [NgModelHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(NgModelHostComponent);
    fixture.detectChanges();
  });

  function inputEl(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('reflects the model value onto the rendered checkbox', async () => {
    fixture.componentInstance.agreed = true;
    fixture.detectChanges();
    // NgModel pushes model -> view updates through a resolved promise (see
    // @angular/forms NgModel#_updateValue) to avoid ExpressionChangedAfterItHasBeenChecked;
    // a microtask must be flushed before the view reflects the new value.
    await fixture.whenStable();
    fixture.detectChanges();

    expect(inputEl().checked).toBe(true);
  });

  it('updates the model when the user toggles the checkbox', () => {
    inputEl().checked = true;
    inputEl().dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.agreed).toBe(true);
  });
});

@Component({
  standalone: true,
  imports: [CheckboxComponent, ReactiveFormsModule],
  template: `<app-checkbox label="Уведомления" [formControl]="control"></app-checkbox>`,
})
class ReactiveFormHostComponent {
  control = new FormControl(false, { nonNullable: true });
}

describe('CheckboxComponent with formControl', () => {
  let fixture: ComponentFixture<ReactiveFormHostComponent>;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [ReactiveFormHostComponent],
    }).compileComponents();

    fixture = TestBed.createComponent(ReactiveFormHostComponent);
    fixture.detectChanges();
  });

  function inputEl(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[type="checkbox"]');
  }

  it('disabling the FormControl disables the rendered checkbox', () => {
    fixture.componentInstance.control.disable();
    fixture.detectChanges();

    expect(inputEl().disabled).toBe(true);
  });

  it('toggling the checkbox updates the FormControl value', () => {
    inputEl().checked = true;
    inputEl().dispatchEvent(new Event('change'));
    fixture.detectChanges();

    expect(fixture.componentInstance.control.value).toBe(true);
  });
});
