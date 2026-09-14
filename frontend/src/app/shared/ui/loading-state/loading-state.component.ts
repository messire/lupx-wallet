import { ChangeDetectionStrategy, Component, input } from '@angular/core';

/**
 * Loading state container (docs/design/ui-kit.md §5): an accessible label
 * ("Loading…" is just an example from the spec — the value is passed by the
 * caller, not hardcoded here) plus arbitrary static skeleton markup via
 * <ng-content> (typically several `app-skeleton`s shaped like the future content).
 */
@Component({
  selector: 'app-loading-state',
  standalone: true,
  template: `
    <div class="app-loading-state" role="status" aria-live="polite">
      <span class="app-loading-state__label">{{ label() }}</span>
      <div class="app-loading-state__shapes" aria-hidden="true">
        <ng-content></ng-content>
      </div>
    </div>
  `,
  styleUrl: './loading-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LoadingStateComponent {
  readonly label = input.required<string>();
}
