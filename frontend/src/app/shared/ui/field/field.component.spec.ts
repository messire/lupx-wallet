import { Component, input } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { FieldComponent } from './field.component';
import { FieldControlDirective } from './field-control.directive';

/**
 * This test host takes its state as inputs (rather than plain mutable fields mutated
 * after creation) so every scenario gets its own fresh `TestBed.createComponent()` +
 * single `detectChanges()`. In this project's zoneless setup (no `zone.js`, no
 * `provideZonelessChangeDetection` either — see `frontend/src/main.ts`/`app.config.ts`),
 * a *second* `detectChanges()` after mutating a plain host field does not reliably
 * re-push updated values into a nested `OnPush` child's signal inputs (verified with an
 * isolated repro outside `FieldComponent` entirely — a framework/tooling characteristic
 * of this app, not a `FieldComponent` bug). Testing each state via a fresh fixture sidesteps
 * that entirely: the *first* `detectChanges()` after `createComponent()` reliably applies
 * all initial bindings.
 */
@Component({
  standalone: true,
  imports: [FieldComponent, FieldControlDirective],
  template: `
    <app-field [label]="label()" [hint]="hint()" [error]="error()" [required]="required()" [disabled]="disabled()">
      <input appFieldControl type="text" />
    </app-field>
  `,
})
class FieldHostComponent {
  readonly label = input('Имя кошелька');
  readonly hint = input<string | null>('Отображается в списке кошельков');
  readonly error = input<string | null>(null);
  readonly required = input(false);
  readonly disabled = input(false);
}

describe('FieldComponent + FieldControlDirective', () => {
  let fixture: ComponentFixture<FieldHostComponent>;

  async function createHost(overrides: Partial<{ label: string; hint: string | null; error: string | null; required: boolean; disabled: boolean }> = {}) {
    await TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [FieldHostComponent] }).compileComponents();
    fixture = TestBed.createComponent(FieldHostComponent);
    for (const [key, value] of Object.entries(overrides)) {
      fixture.componentRef.setInput(key, value);
    }
    fixture.detectChanges();
  }

  function fieldComponent(): FieldComponent {
    return fixture.debugElement.children[0].componentInstance as FieldComponent;
  }

  function inputEl(): HTMLInputElement {
    return fixture.nativeElement.querySelector('input[appFieldControl]');
  }

  function labelEl(): HTMLLabelElement {
    return fixture.nativeElement.querySelector('.app-field__label');
  }

  it('applies the generated controlId to both the label "for" and the input id', async () => {
    await createHost();
    const field = fieldComponent();

    expect(inputEl().id).toBe(field.controlId());
    expect(labelEl().getAttribute('for')).toBe(field.controlId());
  });

  it('describedBy points at the hint id when there is no error', async () => {
    await createHost();
    const field = fieldComponent();

    expect(field.describedBy()).toBe(field.hintId());
    expect(inputEl().getAttribute('aria-describedby')).toBe(field.hintId());
    expect(inputEl().getAttribute('aria-invalid')).toBeNull();
  });

  it('describedBy switches to the error id and aria-invalid=true once an error is set, taking priority over the hint', async () => {
    await createHost({ error: 'Обязательное поле' });
    const field = fieldComponent();

    expect(field.describedBy()).toBe(field.errorId());
    expect(inputEl().getAttribute('aria-describedby')).toBe(field.errorId());
    expect(inputEl().getAttribute('aria-invalid')).toBe('true');
  });

  it('describedBy is null when there is neither hint nor error', async () => {
    await createHost({ hint: null });
    const field = fieldComponent();

    expect(field.describedBy()).toBeNull();
    expect(inputEl().hasAttribute('aria-describedby')).toBe(false);
  });

  it('renders the error text instead of the hint text once invalid', async () => {
    await createHost({ error: 'Обязательное поле' });

    const errorEl = fixture.nativeElement.querySelector('.app-field__error');
    const hintEl = fixture.nativeElement.querySelector('.app-field__hint');

    expect(errorEl?.textContent?.trim()).toBe('Обязательное поле');
    expect(hintEl).toBeNull();
  });

  it('marks the field required with a visible asterisk', async () => {
    await createHost({ required: true });

    expect(fixture.nativeElement.querySelector('.app-field__required')).not.toBeNull();
  });

  it('generates distinct control ids for two separate app-field instances', async () => {
    @Component({
      standalone: true,
      imports: [FieldComponent, FieldControlDirective],
      template: `
        <app-field label="A"><input appFieldControl type="text" /></app-field>
        <app-field label="B"><input appFieldControl type="text" /></app-field>
      `,
    })
    class TwoFieldsHostComponent {}

    await TestBed.resetTestingModule();
    await TestBed.configureTestingModule({ imports: [TwoFieldsHostComponent] }).compileComponents();
    const twoFieldsFixture = TestBed.createComponent(TwoFieldsHostComponent);
    twoFieldsFixture.detectChanges();

    const inputs: HTMLInputElement[] = Array.from(
      twoFieldsFixture.nativeElement.querySelectorAll('input[appFieldControl]'),
    );

    expect(inputs[0].id).not.toBe(inputs[1].id);
  });
});
