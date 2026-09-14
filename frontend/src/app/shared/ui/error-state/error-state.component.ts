import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';

/**
 * Load error with retry (docs/design/ui-kit.md §5): the error text is a component
 * input (not a hardcoded "Failed to load…" string). `retryLabel` can be
 * overridden; the button is hidden via `showRetry` when a retry is not available.
 * Does not replace previously entered user data on screen — that is the caller's
 * responsibility (ui-kit: "an error must not hide an entered value").
 */
@Component({
  selector: 'app-error-state',
  standalone: true,
  templateUrl: './error-state.component.html',
  styleUrl: './error-state.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ErrorStateComponent {
  readonly message = input.required<string>();
  readonly retryLabel = input('Повторить');
  readonly showRetry = input(true);

  readonly retry = output<void>();
}
