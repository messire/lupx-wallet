import { ChangeDetectionStrategy, Component, computed, inject, input } from '@angular/core';
import { RadioGroupComponent } from './radio-group.component';

let nextOptionId = 0;

/**
 * A single option inside `app-radio-group`. The visible label is clickable, the
 * click area is at least 44 px, and the selected state is shown by more than
 * color alone (fill + an inner dot). The group is injected via DI against the
 * actual render hierarchy — this also works when options are passed through a
 * parent's <ng-content>.
 */
@Component({
  selector: 'app-radio-option',
  standalone: true,
  templateUrl: './radio-option.component.html',
  styleUrl: './radio-option.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RadioOptionComponent {
  protected readonly group = inject(RadioGroupComponent);

  readonly value = input.required<string>();
  readonly label = input.required<string>();

  readonly inputId = `app-radio-option-${nextOptionId++}`;
  readonly checked = computed(() => this.group.value() === this.value());
  readonly disabled = computed(() => this.group.disabled());

  onChange(): void {
    this.group.select(this.value());
  }
}
